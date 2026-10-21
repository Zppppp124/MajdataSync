using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MajdataSync;

internal sealed class GitHubAcceleratedDownloader
{
    private const int SegmentCount = 8;
    private const int MaxParallelSegments = 6;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan RouteRetryDelay = TimeSpan.FromSeconds(4);
    private static readonly object RouteGate = new();
    private static readonly Dictionary<string, string> SelectedRoutes =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTimeOffset> MirrorCooldowns =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] BuiltInMirrors =
    [
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://gh.ddlc.top/",
        "https://ghfile.geekertao.top/",
        "https://api.gitproxy.dev/"
    ];
    private static readonly string[] DefaultMirrors = LoadMirrors(
        SettingsStore.Load().GameDownloadMirrors);
    private static IReadOnlyList<string> Mirrors => DefaultMirrors;

    private static readonly TimeSpan MirrorCooldownPeriod = TimeSpan.FromMinutes(15);

    private static string[] LoadMirrors(IEnumerable<string>? configuredMirrors)
    {
        try
        {
            var builtIn = BuiltInMirrors.Select(NormalizeMirror).ToArray();
            var mirrors = (configuredMirrors ?? [])
                .Select(NormalizeMirror)
                .Where(mirror => mirror.Length > 0)
                .Where(mirror => !builtIn.Contains(mirror, StringComparer.OrdinalIgnoreCase))
                .Concat(builtIn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (mirrors.Length > 0)
            {
                return mirrors;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Write("无法读取 GitHub 镜像配置，使用内置镜像", ex);
        }
        return BuiltInMirrors.Select(NormalizeMirror).ToArray();
    }

    private static string NormalizeMirror(string mirror)
    {
        var value = mirror.Trim();
        if (value.Length == 0)
        {
            return "";
        }
        if (value.Equals("https://gitproxy.dev", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("https://gitproxy.dev/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.gitproxy.dev/";
        }
        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }
        return value;
    }

    private static string MirrorUrl(string mirror, string url)
    {
        if (!mirror.Contains("gitproxy.dev", StringComparison.OrdinalIgnoreCase))
        {
            return mirror + url;
        }
        var target = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url["https://".Length..]
            : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? url["http://".Length..]
                : url;
        return mirror + target;
    }

    public static IReadOnlyList<(string Key, string Label, string Url)> Routes(string url) =>
    [
        ("fast", "Fast 加速", ""),
        ("proxy", "Proxy 加速", MirrorUrl(MirrorByToken("gh-proxy.com"), url)),
        ("mirror", "Mirror 加速", MirrorUrl(MirrorByToken("ghproxy.net"), url)),
        ("direct", "直连 GitHub", url)
    ];

    public static IReadOnlyList<string> CandidateUrls(string url) =>
        AvailableMirrors()
            .Select(mirror => MirrorUrl(mirror, url))
            .Append(url)
            .ToArray();

    public static string BuildMirrorUrl(string mirror, string url) =>
        MirrorUrl(NormalizeMirror(mirror), url);

    public static IReadOnlyList<string> DownloadCandidates(
        string url,
        string routePreference = "fast")
    {
        if (url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            return [url];
        }

        var routes = Routes(url);
        if (routePreference.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return CandidateUrls(url);
        }

        if (routePreference.Equals("cnb", StringComparison.OrdinalIgnoreCase) ||
            routePreference.Equals("cnb-stable", StringComparison.OrdinalIgnoreCase))
        {
            return url.StartsWith("https://cnb.cool/", StringComparison.OrdinalIgnoreCase)
                ? [url]
                : CandidateUrls(url);
        }

        if (routePreference.Equals("fast", StringComparison.OrdinalIgnoreCase))
        {
            var ordered = CandidateUrls(url).ToList();
            var fastMirror = ordered.FirstOrDefault(candidate =>
                candidate.StartsWith("https://ghfast.top/", StringComparison.OrdinalIgnoreCase));
            if (fastMirror is not null)
            {
                ordered.Remove(fastMirror);
                ordered.Insert(0, fastMirror);
            }
            return ordered;
        }

        var routeArray = routes.ToArray();
        var selectedIndex = Array.FindIndex(
            routeArray,
            route => route.Key.Equals(routePreference, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        return routeArray
            .Skip(selectedIndex)
            .Concat(routeArray.Take(selectedIndex))
            .Select(route => route.Url)
            .ToArray();
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken,
        string routePreference = "fast")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var preferred = await SelectRouteAsync(
            url,
            cancellationToken,
            destination,
            routePreference);
        var routes = DownloadCandidates(url, routePreference)
            .OrderByDescending(candidate =>
                candidate.Equals(preferred.Url, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => (
                Label: LabelForUrl(candidate),
                Url: candidate))
            .ToList();
        if (routePreference.Equals("cnb-stable", StringComparison.OrdinalIgnoreCase))
        {
            routes = [(Label: "CNB 稳定版", Url: url)];
        }
        if (routes.Count == 0)
        {
            routes = [(Label: "GitHub 直连", Url: url)];
        }

        var routeIndex = 0;
        var routeFailures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = routes[routeIndex % routes.Count];
            WriteRouteMarker(destination, selection);
            progress?.Report((
                selection.Label,
                GetDownloadedBytes(destination),
                null,
                -1));
            try
            {
                await DownloadResumableAsync(
                    destination,
                    selection,
                    progress,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                AppLogger.Write(
                    $"线路 {selection.Label} 下载超时，保留断点并优先重试当前线路",
                    ex);
                progress?.Report((
                    selection.Label,
                    GetDownloadedBytes(destination),
                    null,
                    -1));
                routeFailures++;
                if (routeFailures >= 3)
                {
                    MarkMirrorCooldown(selection.Url);
                    routeFailures = 0;
                    routeIndex++;
                }
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Min(15, routeFailures + 1)),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                AppLogger.Write(
                    $"线路 {selection.Label} 下载波动，保留断点并优先重试当前线路",
                    ex);
                progress?.Report((
                    selection.Label,
                    GetDownloadedBytes(destination),
                    null,
                    -1));
                routeFailures++;
                if (routeFailures >= 3)
                {
                    MarkMirrorCooldown(selection.Url);
                    routeFailures = 0;
                    routeIndex++;
                }
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Min(15, routeFailures + 1)),
                    cancellationToken);
            }
        }
    }

    private static void MarkMirrorCooldown(string url)
    {
        var mirror = Mirrors.FirstOrDefault(prefix =>
            url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (mirror is null)
        {
            return;
        }
        lock (RouteGate)
        {
            MirrorCooldowns[mirror] = DateTimeOffset.Now.Add(MirrorCooldownPeriod);
        }
    }

    private static IReadOnlyList<string> AvailableMirrors()
    {
        var now = DateTimeOffset.Now;
        lock (RouteGate)
        {
            foreach (var expired in MirrorCooldowns
                         .Where(item => item.Value <= now)
                         .Select(item => item.Key)
                         .ToArray())
            {
                MirrorCooldowns.Remove(expired);
            }
            return Mirrors
                .Where(mirror => !MirrorCooldowns.ContainsKey(mirror))
                .ToArray();
        }
    }

    public Task<(string Label, string Url)> SelectRouteAsync(
        string url,
        CancellationToken cancellationToken,
        string? destination = null,
        string routePreference = "fast")
    {
        if (destination is not null &&
            TryReadRouteMarker(destination, out var markerRoute))
        {
            lock (RouteGate)
            {
                SelectedRoutes[url] = markerRoute.Url;
            }
            return Task.FromResult(markerRoute);
        }

        lock (RouteGate)
        {
            if (SelectedRoutes.TryGetValue(url, out var pinned) &&
                CandidateUrls(url).Any(candidate =>
                    candidate.Equals(pinned, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult((LabelForUrl(pinned), pinned));
            }

        }

        if (routePreference.Equals("fast", StringComparison.OrdinalIgnoreCase))
        {
            var configured = CandidateUrls(url).FirstOrDefault(candidate =>
                candidate.StartsWith("https://ghfast.top/", StringComparison.OrdinalIgnoreCase)) ??
                CandidateUrls(url).FirstOrDefault(candidate =>
                    candidate.StartsWith("https://gh-proxy.com/", StringComparison.OrdinalIgnoreCase)) ??
                MirrorUrl(BuiltInMirrors[0], url);
            lock (RouteGate)
            {
                SelectedRoutes[url] = configured;
            }
            return Task.FromResult(("Fast 加速", configured));
        }

        if (routePreference.Equals("cnb", StringComparison.OrdinalIgnoreCase) ||
            routePreference.Equals("cnb-stable", StringComparison.OrdinalIgnoreCase))
        {
            lock (RouteGate)
            {
                SelectedRoutes[url] = url;
            }
            return Task.FromResult(("CNB 稳定版", url));
        }

        if (!routePreference.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var routeKey = new[] { "fast", "proxy", "mirror", "direct" }
                .Contains(routePreference, StringComparer.OrdinalIgnoreCase)
                ? routePreference
                : "fast";
            var configured = Routes(url).First(route =>
                route.Key.Equals(routeKey, StringComparison.OrdinalIgnoreCase));
            lock (RouteGate)
            {
                SelectedRoutes[url] = configured.Url;
            }
            return Task.FromResult((configured.Label, configured.Url));
        }

        var fallback = DownloadCandidates(url, routePreference)
            .Select(candidate => (
                Label: LabelForUrl(candidate),
                Url: candidate))
            .First();
        lock (RouteGate)
        {
            SelectedRoutes[url] = fallback.Url;
        }
        return Task.FromResult(fallback);
    }

    public static string RouteMarkerPath(string destination) => destination + ".source";

    public static void WriteRouteMarker(string destination, (string Label, string Url) route)
    {
        File.WriteAllText(RouteMarkerPath(destination), route.Label + "|" + route.Url);
    }

    public static void ClearRouteMarker(string destination)
    {
        TryDelete(RouteMarkerPath(destination));
    }

    public static void PinRoute(
        string url,
        string destination,
        string label,
        string candidate)
    {
        lock (RouteGate)
        {
            SelectedRoutes[url] = candidate;
        }
        WriteRouteMarker(destination, (label, candidate));
    }

    public static (string Label, string Url)? ReadRouteMarkerForTest(string destination) =>
        TryReadRouteMarker(destination, out var route) ? route : null;

    private static bool TryReadRouteMarker(
        string destination,
        out (string Label, string Url) route)
    {
        var marker = RouteMarkerPath(destination);
        if (!File.Exists(marker))
        {
            route = default;
            return false;
        }

        var value = File.ReadAllText(marker).Trim();
        var separator = value.IndexOf('|');
        if (separator <= 0)
        {
            route = default;
            return false;
        }
        var label = value[..separator];
        var url = value[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
        {
            route = default;
            return false;
        }
        route = (label, url);
        return true;
    }

    private static async Task DownloadResumableAsync(
        string destination,
        (string Label, string Url) candidate,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        using var http = CreateClient();
        var range = await ProbeRangeAsync(http, candidate.Url, cancellationToken);
        if (candidate.Url.StartsWith("https://cnb.cool/", StringComparison.OrdinalIgnoreCase))
        {
            await DownloadSingleResumableAsync(
                http,
                destination,
                candidate,
                progress,
                cancellationToken);
            return;
        }
        if (!range.SupportsRange || range.Total is null)
        {
            await DownloadSingleFromSourceAsync(
                http,
                destination,
                candidate,
                progress,
                cancellationToken);
            return;
        }
        var lengthMarker = destination + ".size";
        var total = range.Total.Value;
        if (File.Exists(lengthMarker) &&
            long.TryParse(File.ReadAllText(lengthMarker), out var knownTotal) &&
            knownTotal > total)
        {
            total = knownTotal;
        }
        await File.WriteAllTextAsync(
            lengthMarker,
            total.ToString(),
            cancellationToken);
        var segments = BuildRanges(total, SegmentCount)
            .Select((item, index) => new Segment(
                index,
                item.Start,
                item.End,
                destination + $".segment{index}"))
            .ToArray();
        foreach (var segment in segments)
        {
            if (!File.Exists(segment.Path))
            {
                continue;
            }

            var length = new FileInfo(segment.Path).Length;
            if (length > segment.Expected)
            {
                using var stream = new FileStream(segment.Path, FileMode.Open, FileAccess.Write);
                stream.SetLength(segment.Expected);
                length = segment.Expected;
            }
            segment.Downloaded = length;
        }

        var received = segments.Select(segment => segment.Downloaded).ToArray();
        var aggregateBytes = received.Sum();
        var speedBytes = 0L;
        var speedWatch = Stopwatch.StartNew();
        var progressGate = new object();
        await Parallel.ForEachAsync(
            segments.Where(segment => segment.Downloaded < segment.Expected),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelSegments,
                CancellationToken = cancellationToken
            },
            async (segment, token) =>
            {
                await DownloadSegmentOnLockedRouteAsync(
                    http,
                    candidate,
                    segment,
                    current =>
                    {
                        received[segment.Index] = current;
                        var aggregate = received.Sum();
                        lock (progressGate)
                        {
                            if (speedWatch.ElapsedMilliseconds < 350)
                            {
                                return;
                            }

                            var speed = (aggregate - aggregateBytes - speedBytes) /
                                        Math.Max(0.001, speedWatch.Elapsed.TotalSeconds);
                            speedBytes = aggregate - aggregateBytes;
                            speedWatch.Restart();
                            progress?.Report((candidate.Label, aggregate, total, speed));
                        }
                    },
                    token);
            });

        foreach (var segment in segments)
        {
            if (!File.Exists(segment.Path) ||
                new FileInfo(segment.Path).Length != segment.Expected)
            {
                throw new IOException($"分片 {segment.Index + 1} 不完整");
            }
        }

        var part = destination + ".part";
        await using (var output = new FileStream(
                         part,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         256 * 1024,
                         FileOptions.Asynchronous))
        {
            foreach (var segment in segments)
            {
                await using var input = File.OpenRead(segment.Path);
                await input.CopyToAsync(output, cancellationToken);
            }
        }

        File.Move(part, destination, overwrite: true);
        foreach (var segment in segments)
        {
            TryDelete(segment.Path);
        }
        ClearRouteMarker(destination);
        progress?.Report((candidate.Label, total, total, 0));
    }

    private static async Task DownloadSegmentOnLockedRouteAsync(
        HttpClient http,
        (string Label, string Url) candidate,
        Segment segment,
        Action<long> updateProgress,
        CancellationToken cancellationToken)
    {
        var lastError = "";
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = segment.Downloaded;
            if (offset >= segment.Expected)
            {
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
                request.Headers.Range = new RangeHeaderValue(
                    segment.Start + offset,
                    segment.End);
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var expectedEnd = response.Content.Headers.ContentRange?.To;
                if (expectedEnd is not null && expectedEnd.Value != segment.End)
                {
                    throw new IOException(
                        $"线路返回了错误的区间 {expectedEnd.Value}，应为 {segment.End}");
                }
                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new IOException("线路不支持稳定分片续传");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    segment.Path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[128 * 1024];
                while (offset < segment.Expected)
                {
                    using var stallCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stallCancellation.CancelAfter(TimeSpan.FromSeconds(35));
                    var wanted = (int)Math.Min(buffer.Length, segment.Expected - offset);
                    var read = await source.ReadAsync(
                        buffer.AsMemory(0, wanted),
                        stallCancellation.Token);
                    if (read == 0)
                    {
                        throw new IOException("线路提前结束分片传输");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    offset += read;
                    segment.Downloaded = offset;
                    updateProgress(offset);
                }
                await output.FlushAsync(cancellationToken);
                return;
            }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                var wait = TimeSpan.FromSeconds(Math.Min(8, 1 + attempt * 0.5));
                await Task.Delay(wait, cancellationToken);
            }
        }

        throw new IOException(
            $"分片 {segment.Index + 1} 在 {candidate.Label} 多次续传失败：{lastError}，断点已保留");
    }

    private static async Task DownloadSingleFromSourceAsync(
        HttpClient http,
        string destination,
        (string Label, string Url) candidate,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        var part = destination + ".part";
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var append = existing > 0 &&
                     response.StatusCode == HttpStatusCode.PartialContent;
        if (existing > 0 && !append)
        {
            existing = 0;
        }

        var total = response.Content.Headers.ContentRange?.Length ??
                    (response.Content.Headers.ContentLength is { } length
                        ? (append ? existing + length : length)
                        : null);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            part,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        var received = existing;
        var speedWatch = Stopwatch.StartNew();
        var speedBytes = 0L;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            speedBytes += read;
            if (speedWatch.ElapsedMilliseconds >= 350)
            {
                progress?.Report((
                    candidate.Label,
                    received,
                    total,
                    speedBytes / Math.Max(0.001, speedWatch.Elapsed.TotalSeconds)));
                speedBytes = 0;
                speedWatch.Restart();
            }
        }
        await output.FlushAsync(cancellationToken);
        await output.DisposeAsync();
        if (total is not null && new FileInfo(part).Length != total.Value)
        {
            throw new IOException("下载不完整，断点已保留");
        }
        var actualLength = new FileInfo(part).Length;
        if (total is null || actualLength != total.Value)
        {
            throw new IOException(
                $"单流下载未完成：{actualLength:N0} / {total?.ToString("N0") ?? "未知"} 字节，断点已保留");
        }
        File.Move(part, destination, overwrite: true);
        if (total is not null)
        {
            await File.WriteAllTextAsync(
                destination + ".size",
                total.Value.ToString(),
                cancellationToken);
        }
        ClearRouteMarker(destination);
        progress?.Report((candidate.Label, received, total ?? received, 0));
    }

    private static async Task DownloadSingleResumableAsync(
        HttpClient http,
        string destination,
        (string Label, string Url) candidate,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        var part = destination + ".part";
        var failures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Url);
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                await using var output = new FileStream(
                    part,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    256 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 1024];
                var received = 0L;
                var speedWatch = Stopwatch.StartNew();
                var speedBytes = 0L;
                while (true)
                {
                    using var stallCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stallCancellation.CancelAfter(TimeSpan.FromSeconds(45));
                    var read = await source.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        stallCancellation.Token);
                    if (read == 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    speedBytes += read;
                    failures = 0;
                    if (speedWatch.ElapsedMilliseconds >= 350)
                    {
                        progress?.Report((
                            candidate.Label,
                            received,
                            total,
                            speedBytes / Math.Max(0.001, speedWatch.Elapsed.TotalSeconds)));
                        speedBytes = 0;
                        speedWatch.Restart();
                    }
                }
                await output.FlushAsync(cancellationToken);
                var completed = new FileInfo(part).Length;
                if (total is null || completed == total.Value)
                {
                    var previous = File.Exists(destination)
                        ? new FileInfo(destination).Length
                        : -1;
                    if (completed <= 0 || completed == previous)
                    {
                        throw new IOException("CNB 返回了空的或重复的临时文件");
                    }
                    File.Move(part, destination, overwrite: true);
                    await File.WriteAllTextAsync(
                        destination + ".size",
                        completed.ToString(),
                        cancellationToken);
                    ClearRouteMarker(destination);
                    progress?.Report((candidate.Label, completed, completed, 0));
                    return;
                }
                throw new IOException(
                    $"单流下载未完成：{completed:N0} / {total?.ToString("N0") ?? "未知"} 字节");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                var existingBytes = File.Exists(part)
                    ? new FileInfo(part).Length
                    : 0;
                AppLogger.Write(
                    $"CNB 下载波动，重新连接（该服务不支持断点，已下载临时文件将清理）",
                    ex);
                TryDelete(part);
                progress?.Report((
                    candidate.Label,
                    existingBytes,
                    null,
                    existingBytes > 0 ? -2 : -1));
                if (failures >= 2)
                {
                    throw new IOException(
                        "CNB 连接中断，该线路不支持断点续传；请稍后重试稳定版下载",
                        ex);
                }
                await Task.Delay(
                    TimeSpan.FromSeconds(failures * 2),
                    cancellationToken);
            }
        }
    }

    private static async Task<(bool SupportsRange, long? Total)> ProbeRangeAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
            !IsMirrorUrl(url) &&
            !url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return (false, response.Content.Headers.ContentLength);
        }
        return (true, response.Content.Headers.ContentRange?.Length);
    }

    private static List<(long Start, long End)> BuildRanges(long total, int count)
    {
        var ranges = new List<(long Start, long End)>();
        var size = total / count;
        for (var index = 0; index < count; index++)
        {
            var start = index * size;
            var end = index == count - 1 ? total - 1 : start + size - 1;
            ranges.Add((start, end));
        }
        return ranges;
    }

    private static long GetDownloadedBytes(string destination)
    {
        var total = 0L;
        for (var index = 0; index < SegmentCount; index++)
        {
            var path = destination + $".segment{index}";
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }
        var part = destination + ".part";
        if (File.Exists(part))
        {
            total += new FileInfo(part).Length;
        }
        return total;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(12),
            MaxConnectionsPerServer = 12,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 MajdataSync/1.8.2");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        return http;
    }

    private static string MirrorLabel(string mirror) =>
        mirror.Contains("gitproxy.dev", StringComparison.OrdinalIgnoreCase) ? "GitProxy 加速" :
        mirror.Contains("ghfast", StringComparison.OrdinalIgnoreCase) ? "Fast 加速" :
        mirror.Contains("gh-proxy", StringComparison.OrdinalIgnoreCase) ? "Proxy 加速" :
        mirror.Contains("ghproxy.net", StringComparison.OrdinalIgnoreCase) ? "Mirror 加速" :
        "GitHub 加速";

    private static string MirrorByToken(string token) =>
        Mirrors.FirstOrDefault(mirror =>
            mirror.Contains(token, StringComparison.OrdinalIgnoreCase)) ??
        Mirrors.First();

    private static bool IsMirrorUrl(string url) =>
        Mirrors.Any(mirror =>
            url.StartsWith(mirror, StringComparison.OrdinalIgnoreCase));

    private static string LabelForUrl(string url)
    {
        if (!IsMirrorUrl(url) && !url.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub 加速";
        }
        if (!IsMirrorUrl(url))
        {
            return "直连 GitHub";
        }
        return MirrorLabel(
            Mirrors.First(mirror =>
                url.StartsWith(mirror, StringComparison.OrdinalIgnoreCase)));
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
        }
    }

    private sealed class Segment(
        int index,
        long start,
        long end,
        string path)
    {
        public int Index { get; } = index;
        public long Start { get; } = start;
        public long End { get; } = end;
        public string Path { get; } = path;
        public long Expected { get; } = end - start + 1;
        public long Downloaded { get; set; }
    }
}
