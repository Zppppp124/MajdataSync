using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MajdataSync;

internal sealed class NetworkOptimizer
{
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MajdataSync");

    private static readonly string RouteCachePath = Path.Combine(CacheFolder, "network-routes.json");
    private static readonly string[] DnsResolvers = ["1.1.1.1", "8.8.8.8", "223.5.5.5"];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<string, NetworkRoute> _routes = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public IReadOnlyList<SiteRoute> SiteRoutes
    {
        get
        {
            lock (_routes)
            {
                return _routes.Values
                    .Select(route => route.ToSiteRoute())
                    .OrderBy(route => route.LatencyMs)
                    .ToArray();
            }
        }
    }

    public async Task<IReadOnlyList<SiteRoute>> RefreshAsync(
        bool includeMirrors,
        int port,
        CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var hosts = includeMirrors
                ? MirrorSelector.Mirrors.Append(MirrorSelector.PrimaryHost).ToArray()
                : [MirrorSelector.PrimaryHost];
            var tasks = hosts.Select(host => MeasureSiteAsync(host, port, cancellationToken)).ToArray();
            var measured = await Task.WhenAll(tasks);
            lock (_routes)
            {
                _routes.Clear();
                foreach (var route in measured)
                {
                    _routes[route.Host] = route;
                }
            }

            _lastRefresh = DateTimeOffset.Now;
            SaveRoutes();
            return SiteRoutes;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<IReadOnlyList<SiteRoute>> EnsureAsync(
        bool includeMirrors,
        int port,
        CancellationToken cancellationToken)
    {
        lock (_routes)
        {
            if (_routes.Count > 0 && DateTimeOffset.Now - _lastRefresh < TimeSpan.FromMinutes(10))
            {
                return _routes.Values
                    .Where(route => includeMirrors || route.Host.Equals(MirrorSelector.PrimaryHost))
                    .Select(route => route.ToSiteRoute())
                    .OrderBy(route => route.LatencyMs)
                    .ToArray();
            }
        }

        return await RefreshAsync(includeMirrors, port, cancellationToken);
    }

    public async Task<IReadOnlyList<IPAddress>> GetAddressesAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(includeMirrors: true, port, cancellationToken);
        lock (_routes)
        {
            if (_routes.TryGetValue(host, out var route) && route.Addresses.Count > 0)
            {
                return route.Addresses.ToArray();
            }
        }

        var measuredRoute = await MeasureSiteAsync(host, port, cancellationToken);
        lock (_routes)
        {
            _routes[host] = measuredRoute;
        }
        return measuredRoute.Addresses.ToArray();
    }

    public void MarkFailed(string host, IPAddress address)
    {
        lock (_routes)
        {
            if (!_routes.TryGetValue(host, out var route))
            {
                return;
            }

            var remaining = route.Addresses
                .Where(item => !item.Equals(address))
                .ToArray();
            _routes[host] = route with { Addresses = remaining };
        }
    }

    public void ForceRefresh()
    {
        _lastRefresh = DateTimeOffset.MinValue;
    }

    private async Task<NetworkRoute> MeasureSiteAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var candidates = await ResolveCandidatesAsync(host, cancellationToken);
        var measured = await MeasureAddressesAsync(candidates, port, cancellationToken);
        var addresses = measured
            .Where(item => item.Latency is not null)
            .OrderBy(item => item.Latency)
            .Select(item => item.Address)
            .ToList();
        var latency = measured
            .Where(item => item.Latency is not null)
            .Select(item => item.Latency!.Value)
            .DefaultIfEmpty(long.MaxValue)
            .Min();
        return new NetworkRoute
        {
            Host = host,
            LatencyMs = addresses.Count > 0 ? latency : long.MaxValue,
            Addresses = addresses,
            Available = addresses.Count > 0
        };
    }

    private static async Task<List<IPAddress>> ResolveCandidatesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<IPAddress>();
        try
        {
            foreach (var address in await Dns.GetHostAddressesAsync(host, cancellationToken))
            {
                if (IsIpv4(address))
                {
                    found.Add(Normalize(address));
                }
            }
        }
        catch
        {
            // Public resolvers below can still answer.
        }

        await Task.WhenAll(DnsResolvers.Select(resolver =>
            QueryDnsAsync(found, host, resolver, cancellationToken)));
        return found.ToList();
    }

    private static async Task QueryDnsAsync(
        HashSet<IPAddress> found,
        string host,
        string resolver,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Connect(IPAddress.Parse(resolver), 53);
            await udp.SendAsync(BuildDnsQuery(host), timeout.Token);
            var response = await udp.ReceiveAsync(timeout.Token);
            lock (found)
            {
                foreach (var address in ParseDnsAnswers(response.Buffer))
                {
                    if (IsIpv4(address))
                    {
                        found.Add(Normalize(address));
                    }
                }
            }
        }
        catch
        {
            // One resolver may be blocked; the other sources remain.
        }
    }

    private static byte[] BuildDnsQuery(string host)
    {
        var query = new List<byte>
        {
            0x4d, 0x4a, 0x01, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        foreach (var label in host.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            query.Add((byte)bytes.Length);
            query.AddRange(bytes);
        }

        query.Add(0);
        query.AddRange([0x00, 0x01, 0x00, 0x01]);
        return query.ToArray();
    }

    private static IEnumerable<IPAddress> ParseDnsAnswers(byte[] data)
    {
        if (data.Length < 12)
        {
            yield break;
        }

        var answerCount = (data[6] << 8) | data[7];
        var offset = 12;
        while (offset < data.Length && data[offset] != 0)
        {
            offset += data[offset] + 1;
        }

        offset += 5;
        for (var i = 0; i < answerCount && offset + 12 <= data.Length; i++)
        {
            if ((data[offset] & 0xc0) == 0xc0)
            {
                offset += 2;
            }
            else
            {
                while (offset < data.Length && data[offset] != 0)
                {
                    offset += data[offset] + 1;
                }
                offset++;
            }

            if (offset + 10 > data.Length)
            {
                yield break;
            }

            var type = (data[offset] << 8) | data[offset + 1];
            var length = (data[offset + 8] << 8) | data[offset + 9];
            offset += 10;
            if (offset + length > data.Length)
            {
                yield break;
            }

            if (type == 1 && length == 4)
            {
                yield return new IPAddress(data.AsSpan(offset, 4));
            }
            offset += length;
        }
    }

    private static async Task<List<MeasuredAddress>> MeasureAddressesAsync(
        IEnumerable<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        var tasks = addresses.Distinct().Take(32)
            .Select(address => MeasureAddressAsync(address, port, cancellationToken));
        return (await Task.WhenAll(tasks)).ToList();
    }

    private static async Task<MeasuredAddress> MeasureAddressAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token);
            return new MeasuredAddress(address, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MeasuredAddress(address, null);
        }
        catch
        {
            return new MeasuredAddress(address, null);
        }
    }

    private void SaveRoutes()
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            var payload = new RouteCache
            {
                UpdatedAt = DateTimeOffset.Now,
                Routes = SiteRoutes.Select(route => new CachedRoute
                {
                    Host = route.Host,
                    LatencyMs = route.LatencyMs,
                    Addresses = route.Addresses.Select(address => address.ToString()).ToList()
                }).ToList()
            };
            var temp = RouteCachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(payload, JsonOptions.Default));
            File.Move(temp, RouteCachePath, overwrite: true);
        }
        catch
        {
            // Route cache is optional.
        }
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool IsIpv4(IPAddress address) =>
        Normalize(address).AddressFamily == AddressFamily.InterNetwork;

    private sealed record MeasuredAddress(IPAddress Address, long? Latency);

    private sealed record NetworkRoute
    {
        public required string Host { get; init; }
        public long LatencyMs { get; init; }
        public required IReadOnlyList<IPAddress> Addresses { get; init; }
        public bool Available { get; init; }

        public SiteRoute ToSiteRoute() => new()
        {
            Host = Host,
            LatencyMs = LatencyMs,
            Addresses = Addresses,
            Available = Available
        };
    }

    private sealed class RouteCache
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public List<CachedRoute> Routes { get; set; } = [];
    }

    private sealed class CachedRoute
    {
        public string Host { get; set; } = "";
        public long LatencyMs { get; set; }
        public List<string> Addresses { get; set; } = [];
    }
}
