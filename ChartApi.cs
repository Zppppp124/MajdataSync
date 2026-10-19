using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataSync;

internal sealed class ChartApi : IDisposable
{
    public const string SiteUrl = "https://majdata.net";
    public const string ApiRoot = SiteUrl + "/api3/api";
    private const int PageSize = 30;
    private const int ScanParallelism = 12;
    private const int MaxConsecutiveEmptyPages = 2;
    private readonly HttpClient _http;
    private readonly Dictionary<string, HttpClient> _mirrorClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SocketsHttpHandler _handler;
    private readonly Uri _siteUri = new(SiteUrl);
    private readonly NetworkOptimizer _network = new();
    private int _consecutiveFailures;
    private string? _lastGoodMirror;
    private string? _lastIndexHost;
    private readonly HashSet<string> _sessionFailedHosts = new(StringComparer.OrdinalIgnoreCase);

    public ChartApi()
    {
        _handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 16,
            ConnectCallback = ConnectToReachableAddressAsync
        };
        _http = new HttpClient(_handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 MajdataSync/1.0");
        _http.DefaultRequestHeaders.Referrer = new Uri(SiteUrl + "/");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
    }

    public static string ChartRoot(string id) => $"{ApiRoot}/maichart/{Uri.EscapeDataString(id)}";

    public static string Image(string id, bool full) =>
        ChartRoot(id) + "/image" + (full ? "?fullImage=true" : "");

    public static string Track(string id) => ChartRoot(id) + "/track";
    public static string Maidata(string id) => ChartRoot(id) + "/chart";
    public static string Video(string id) => ChartRoot(id) + "/video";

    public string? BestNode { get; private set; }

    public async Task<IReadOnlyList<SiteRoute>> RefreshNetworkNodesAsync(CancellationToken cancellationToken)
    {
        _network.ForceRefresh();
        var routes = await _network.RefreshAsync(
            SettingsStore.Load().UseMirrors,
            443,
            cancellationToken);
        BestNode = routes.FirstOrDefault(route => route.Available)?.Addresses.FirstOrDefault()?.ToString();
        _lastIndexHost = routes.FirstOrDefault(route => route.Available)?.Host;
        return routes;
    }

    public async Task<IReadOnlyList<SiteRoute>> EnsureNetworkRoutesAsync(CancellationToken cancellationToken)
    {
        var routes = await _network.EnsureAsync(
            SettingsStore.Load().UseMirrors,
            443,
            cancellationToken);
        BestNode = routes.FirstOrDefault(route => route.Available)?.Addresses.FirstOrDefault()?.ToString();
        return routes;
    }

    public string? LastGoodMirror => _lastGoodMirror;

    public string? LastIndexHost => _lastIndexHost;

    public Task<IReadOnlyList<SiteRoute>> GetNetworkRoutesAsync(CancellationToken cancellationToken) =>
        _network.EnsureAsync(SettingsStore.Load().UseMirrors, 443, cancellationToken);

    internal string[] MirrorHosts()
    {
        var routes = _network.SiteRoutes;
        if (routes.Count == 0)
        {
            return SettingsStore.Load().UseMirrors
                ? MirrorSelector.Mirrors.Append(MirrorSelector.PrimaryHost).ToArray()
                : [MirrorSelector.PrimaryHost];
        }

        return routes
            .Where(route => route.Available)
            .OrderBy(route => route.LatencyMs)
            .Select(route => route.Host)
            .ToArray();
    }

    internal async Task<string[]> MirrorHostsForDownloadAsync(CancellationToken cancellationToken)
    {
        await _network.EnsureAsync(
            SettingsStore.Load().UseMirrors,
            443,
            cancellationToken);

        var hosts = MirrorHosts()
            .Where(host => !_sessionFailedHosts.Contains(host))
            .ToList();
        if (hosts.Count == 0)
        {
            hosts.Add(_lastGoodMirror ?? MirrorSelector.PrimaryHost);
        }
        if (!string.IsNullOrWhiteSpace(_lastGoodMirror))
        {
            hosts.Remove(_lastGoodMirror);
            hosts.Insert(0, _lastGoodMirror);
        }

        return hosts.ToArray();
    }

    public async Task<(string Host, List<ChartItem> Items)> FetchCatalogPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        await _network.EnsureAsync(
            SettingsStore.Load().UseMirrors,
            443,
            cancellationToken);
        var errors = new List<string>();
        var hosts = MirrorHosts().ToList();
        if (!string.IsNullOrWhiteSpace(_lastIndexHost) && hosts.Contains(_lastIndexHost))
        {
            hosts.Remove(_lastIndexHost);
            hosts.Insert(0, _lastIndexHost);
        }

        foreach (var host in hosts)
        {
            if (_sessionFailedHosts.Contains(host))
            {
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var url = $"{MirrorSelector.ApiRoot(host)}/maichart/list" +
                          $"?sort=&page={page.ToString(CultureInfo.InvariantCulture)}&search=";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri($"https://{host}/");
                using var response = await MirrorClient(host).SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{host}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var items = await JsonSerializer.DeserializeAsync<List<ChartItem>>(
                    stream,
                    JsonOptions.Default,
                    timeout.Token) ?? [];
                if (items.Count > 0)
                {
                    _lastGoodMirror = host;
                    _lastIndexHost = host;
                    return (host, items);
                }

                errors.Add($"{host}: 空页");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{host}: 超时");
                _sessionFailedHosts.Add(host);
            }
            catch (Exception ex)
            {
                errors.Add($"{host}: {ex.Message}");
                _sessionFailedHosts.Add(host);
            }
        }

        throw new HttpRequestException(
            "所有谱面索引地址均不可用：" + string.Join("；", errors));
    }

    private HttpClient MirrorClient(string host)
    {
        lock (_mirrorClients)
        {
            if (_mirrorClients.TryGetValue(host, out var existing))
            {
                return existing;
            }

            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(8),
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 12,
                ConnectCallback = ConnectToSiteAddressAsync
            };
            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 MajdataSync/1.0");
            client.DefaultRequestHeaders.Referrer = new Uri($"https://{host}/");
            _mirrorClients[host] = client;
            return client;
        }
    }

    private async ValueTask<Stream> ConnectToSiteAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await _network.GetAddressesAsync(
            context.DnsEndPoint.Host,
            context.DnsEndPoint.Port,
            cancellationToken);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            try
            {
                return await ConnectAddressAsync(context.DnsEndPoint, address, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _network.MarkFailed(context.DnsEndPoint.Host, address);
            }
        }

        throw lastError ?? new HttpRequestException("站点没有可用 IP");
    }

    public static string ResourceUrl(string host, string chartId, string resource)
    {
        var query = resource == "image" ? "?fullImage=true" : "";
        return $"{MirrorSelector.ChartRoot(host, chartId)}/{resource}{query}";
    }

    private async ValueTask<Stream> ConnectToReachableAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await _network.GetAddressesAsync(
                context.DnsEndPoint.Host,
                context.DnsEndPoint.Port,
                cancellationToken);
        }
        catch
        {
            addresses = [];
        }

        if (addresses.Count == 0)
        {
            return await ConnectAddressAsync(context.DnsEndPoint, null, cancellationToken);
        }

        Exception? lastError = null;
        foreach (var address in addresses.Where(IsReachableAddress))
        {
            try
            {
                var stream = await ConnectAddressAsync(context.DnsEndPoint, address, cancellationToken);
                BestNode = address.ToString();
                return stream;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _network.MarkFailed(context.DnsEndPoint.Host, address);
            }
        }

        throw lastError ?? new HttpRequestException("没有可用的站点连接地址");
    }

    private async ValueTask<Stream> ConnectAddressAsync(
        DnsEndPoint endpoint,
        IPAddress? address,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            var resolved = address ??
                           (await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken))
                           .FirstOrDefault(IsReachableAddress) ??
                           (await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken))[0];
            var target = new IPEndPoint(resolved, endpoint.Port);
            await socket.ConnectAsync(target, cancellationToken);
            var stream = new NetworkStream(socket, ownsSocket: true);
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = endpoint.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                },
                cancellationToken);
            return ssl;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsReachableAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetwork;
    }

    public async Task<List<ChartItem>> GetAllChartsAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Report("正在读取站点曲目索引…");
        var firstPage = await FetchPageAsync(0, cancellationToken);
        if (firstPage.Count == 0)
        {
            return [];
        }

        var results = new Dictionary<string, ChartItem>(StringComparer.OrdinalIgnoreCase);
        AddPage(results, firstPage);
        var page = 1;
        var consecutiveEmptyPages = 0;

        while (consecutiveEmptyPages < MaxConsecutiveEmptyPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pages = Enumerable.Range(page, ScanParallelism).ToArray();
            var tasks = pages.Select(p => FetchPageSafeAsync(p, cancellationToken)).ToArray();
            var pageResults = await Task.WhenAll(tasks);

            foreach (var item in pageResults)
            {
                if (item.Count == 0)
                {
                    consecutiveEmptyPages++;
                }
                else
                {
                    consecutiveEmptyPages = 0;
                    AddPage(results, item);
                }
            }

            if (pageResults.All(item => item.Count == 0))
            {
                var retryTasks = pages
                    .Select(p => FetchPageSafeAsync(p, cancellationToken))
                    .ToArray();
                foreach (var item in await Task.WhenAll(retryTasks))
                {
                    if (item.Count > 0)
                    {
                        consecutiveEmptyPages = 0;
                        AddPage(results, item);
                    }
                }
            }

            page += ScanParallelism;
            status?.Report($"正在读取站点曲目索引… 已发现 {results.Count:N0} 首");
        }

        status?.Report($"索引完成，共发现 {results.Count:N0} 首曲目");
        return results.Values
            .OrderByDescending(chart => chart.Timestamp)
            .ThenBy(chart => chart.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddPage(Dictionary<string, ChartItem> results, IEnumerable<ChartItem> page)
    {
        foreach (var chart in page)
        {
            if (!string.IsNullOrWhiteSpace(chart.Id))
            {
                results[chart.Id] = chart;
            }
        }
    }

    private async Task<List<ChartItem>> FetchPageSafeAsync(int page, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return await FetchPageAsync(page, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (attempt < 3)
            {
                var sitePressure = Volatile.Read(ref _consecutiveFailures) >= 3;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(sitePressure ? 2600 : 450 * (attempt + 1)),
                    cancellationToken);
            }
            catch
            {
                // A permanently unavailable page should not abort a long scan.
                return [];
            }
        }

        return [];
    }

    private async Task<List<ChartItem>> FetchPageAsync(int page, CancellationToken cancellationToken)
    {
        try
        {
            var result = await FetchCatalogPageAsync(page, cancellationToken);
            var items = result.Items;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return items;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Interlocked.Increment(ref _consecutiveFailures);
            throw;
        }
    }

    public async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        int segmentCount = 0)
    {
        var uri = new Uri(url);
        if (!uri.Host.Equals("majdata.net", StringComparison.OrdinalIgnoreCase))
        {
            var addresses = await _network.GetAddressesAsync(uri.Host, uri.Port, cancellationToken);
            using var mirrorHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                MaxConnectionsPerServer = 8,
                ConnectCallback = async (context, token) =>
                {
                    var candidates = addresses.Count > 0
                        ? addresses
                        : await _network.GetAddressesAsync(uri.Host, uri.Port, token);
                    Exception? lastError = null;
                    foreach (var address in candidates)
                    {
                        try
                        {
                            return await ConnectAddressAsync(context.DnsEndPoint, address, token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            _network.MarkFailed(uri.Host, address);
                        }
                    }

                    throw lastError ?? new HttpRequestException("镜像没有可用 IP");
                }
            };
            using var mirrorHttp = new HttpClient(mirrorHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            mirrorHttp.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 MajdataSync/1.0");
            mirrorHttp.DefaultRequestHeaders.Referrer = new Uri($"https://{uri.Host}/");
            await DownloadWithClientAsync(
                mirrorHttp,
                url,
                destination,
                progress,
                cancellationToken,
                segmentCount);
            return;
        }

        await DownloadWithClientAsync(
            _http,
            url,
            destination,
            progress,
            cancellationToken,
            segmentCount);
    }

    private async Task DownloadWithClientAsync(
        HttpClient client,
        string url,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        int segmentCount)
    {
        if (segmentCount <= 0)
        {
            var mode = SettingsStore.Load().DownloadMode;
            segmentCount = mode switch
            {
                "single" => 1,
                "2" => 2,
                "4" => 4,
                _ => 2
            };
        }

        if (segmentCount > 1)
        {
            try
            {
                if (await TrySegmentedDownloadAsync(
                        client,
                        url,
                        destination,
                        progress,
                        segmentCount,
                        cancellationToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Some CDN nodes disable Range; retry with a single connection.
            }
        }

        await DownloadSingleAsync(client, url, destination, progress, cancellationToken);
    }

    private async Task DownloadSingleAsync(
        HttpClient client,
        string url,
        string destination,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase) && total < 100_000)
            {
                throw new InvalidDataException("站点返回了网页而不是资源，请稍后重试");
            }

            long received;
            double speed = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                received = 0;
                var meter = System.Diagnostics.Stopwatch.StartNew();
                long meterBytes = 0;
                var lastProgress = System.Diagnostics.Stopwatch.StartNew();

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    meterBytes += read;

                    if (meter.ElapsedMilliseconds >= 250)
                    {
                        speed = meterBytes / Math.Max(0.001, meter.Elapsed.TotalSeconds);
                        meter.Restart();
                        meterBytes = 0;
                        lastProgress.Restart();
                        progress?.Report(new DownloadProgress(received, total, speed));
                    }

                    if (lastProgress.ElapsedMilliseconds >= 90_000)
                    {
                        throw new IOException("下载超时：90 秒内没有收到新数据");
                    }
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(temp, destination, overwrite: true);
            progress?.Report(new DownloadProgress(received, total ?? received, speed));
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private async Task<bool> TrySegmentedDownloadAsync(
        HttpClient client,
        string url,
        string destination,
        IProgress<DownloadProgress>? progress,
        int requestedSegments,
        CancellationToken cancellationToken)
    {
        long? total;
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(TimeSpan.FromSeconds(18));
        using (var probe = new HttpRequestMessage(HttpMethod.Get, url))
        {
            probe.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(
                probe,
                HttpCompletionOption.ResponseHeadersRead,
                probeCancellation.Token);
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                _network.ForceRefresh();
                return false;
            }

            total = response.Content.Headers.ContentRange?.Length;
        }

        if (total is null || total < 3 * 1024 * 1024)
        {
            return false;
        }

        var totalBytes = total.Value;
            var segmentCount = Math.Clamp(requestedSegments, 2, 4);
            if (totalBytes >= 60L * 1024 * 1024)
            {
                segmentCount = Math.Min(segmentCount + 1, 4);
            }
        var segmentSize = (totalBytes + segmentCount - 1) / segmentCount;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var parts = Enumerable.Range(0, segmentCount)
            .Select(index => destination + $".part{index}")
            .ToArray();
        using var transferTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transferTimeout.CancelAfter(TimeSpan.FromMinutes(10));
        var meter = System.Diagnostics.Stopwatch.StartNew();
        long meterBytes = 0;
        long totalReceived = 0;
        double speed = 0;

        try
        {
            var receivedPerPart = new long[segmentCount];
            var tasks = Enumerable.Range(0, segmentCount).Select(async index =>
            {
                var from = index * segmentSize;
                var to = Math.Min(totalBytes - 1, from + segmentSize - 1);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new InvalidDataException("站点未接受分段下载请求");
                }

                await using var source = await response.Content.ReadAsStreamAsync(transferTimeout.Token);
                await using var output = new FileStream(
                    parts[index],
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, transferTimeout.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), transferTimeout.Token);
                    Interlocked.Add(ref receivedPerPart[index], read);
                    var received = Interlocked.Add(ref totalReceived, read);
                    Interlocked.Add(ref meterBytes, read);
                    if (meter.ElapsedMilliseconds >= 250)
                    {
                        var bytesThisSample = Interlocked.Exchange(ref meterBytes, 0);
                        speed = bytesThisSample / Math.Max(0.001, meter.Elapsed.TotalSeconds);
                        meter.Restart();
                        progress?.Report(new DownloadProgress(received, totalBytes, speed));
                    }
                }

                await output.FlushAsync(cancellationToken);
            }).ToArray();

            await Task.WhenAll(tasks);
            var expectedBytes = parts.Sum(part => new FileInfo(part).Length);
            if (expectedBytes != totalBytes)
            {
                throw new InvalidDataException(
                    $"分段下载不完整：收到 {expectedBytes}，应为 {totalBytes}");
            }

            var temp = destination + ".part";
            await using (var combined = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             256 * 1024,
                             FileOptions.Asynchronous))
            {
                foreach (var part in parts)
                {
                    await using var input = File.OpenRead(part);
                    await input.CopyToAsync(combined, 256 * 1024, cancellationToken);
                }

                await combined.FlushAsync(cancellationToken);
            }

            File.Move(temp, destination, overwrite: true);
            progress?.Report(new DownloadProgress(totalBytes, totalBytes, 0));
            return true;
        }
        finally
        {
            foreach (var part in parts)
            {
                TryDelete(part);
            }
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        lock (_mirrorClients)
        {
            foreach (var client in _mirrorClients.Values)
            {
                client.Dispose();
            }
            _mirrorClients.Clear();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best effort.
        }
    }
}
