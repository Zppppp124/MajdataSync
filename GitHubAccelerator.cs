using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MajdataSync;

internal sealed class GitHubAcceleratedDownloader
{
    private const int SegmentCount = 8;
    private const int MaxParallelSegments = 6;
    private const int ProbeBytes = 512 * 1024;
    private static readonly string[] Mirrors =
    [
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/"
    ];

    public static IReadOnlyList<string> CandidateUrls(string url) =>
        Mirrors
            .Select(mirror => mirror + url)
            .Append(url)
            .ToArray();

    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var candidates = await GetCandidateOrderAsync(url, cancellationToken);
        Exception? lastError = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            try
            {
                progress?.Report((candidate.Label, GetDownloadedBytes(destination), null, 0));
                await DownloadResumableAsync(
                    destination,
                    candidate,
                    progress,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                AppLogger.Write(
                    $"GitHub 下载线路 {candidate.Label} 失败，准备切换下一线路",
                    ex);
                if (index + 1 < candidates.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
            }
        }

        throw new IOException("GitHub 加速线路均下载失败，已保留断点", lastError);
    }

    public async Task<List<(string Label, string Url)>> GetCandidateOrderAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var ranked = await RankCandidatesAsync(url, cancellationToken);
        if (ranked.Count > 0)
        {
            return ranked;
        }

        return CandidateUrls(url)
            .Select(candidate => (
                Label: candidate.Equals(url, StringComparison.Ordinal)
                    ? "直连 GitHub"
                    : MirrorLabel(Mirrors.First(mirror =>
                        candidate.StartsWith(mirror, StringComparison.Ordinal))),
                Url: candidate))
            .ToList();
    }

    public static async Task<List<(string Label, string Url)>> RankCandidatesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var http = CreateClient();
        var probes = CandidateUrls(url)
            .Select(candidate => (
                Label: candidate.Equals(url, StringComparison.Ordinal)
                    ? "直连 GitHub"
                    : MirrorLabel(Mirrors.First(mirror =>
                        candidate.StartsWith(mirror, StringComparison.Ordinal))),
                Url: candidate));
        var results = await Task.WhenAll(probes.Select(probe =>
            ProbeAsync(http, probe.Label, probe.Url, cancellationToken)));
        return results
            .Where(result => result.Speed > 0)
            .OrderByDescending(result => result.Speed)
            .Select(result => (result.Label, result.Url))
            .ToList();
    }

    private static async Task<(string Label, string Url, double Speed)> ProbeAsync(
        HttpClient http,
        string label,
        string url,
        CancellationToken cancellationToken)
    {
        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        probeCancellation.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, ProbeBytes - 1);
            var stopwatch = Stopwatch.StartNew();
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                probeCancellation.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(
                probeCancellation.Token);
            var buffer = new byte[64 * 1024];
            var total = 0;
            while (total < ProbeBytes)
            {
                var read = await stream.ReadAsync(buffer, probeCancellation.Token);
                if (read == 0)
                {
                    break;
                }
                total += read;
            }
            stopwatch.Stop();
            var speed = total / Math.Max(0.01, stopwatch.Elapsed.TotalSeconds);
            return total >= 256 * 1024
                ? (label, url, speed)
                : (label, url, 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (label, url, 0);
        }
        catch
        {
            return (label, url, 0);
        }
    }

    private static async Task DownloadResumableAsync(
        string destination,
        (string Label, string Url) candidate,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        using var http = CreateClient();
        var range = await ProbeRangeAsync(http, candidate.Url, cancellationToken);
        if (!range.SupportsRange || range.Total is null or < 1_000_000)
        {
            await DownloadSingleFromSourceAsync(
                http,
                destination,
                candidate,
                progress,
                cancellationToken);
            return;
        }

        var total = range.Total.Value;
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
        for (var attempt = 0; attempt < 6; attempt++)
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
                    stallCancellation.CancelAfter(TimeSpan.FromSeconds(20));
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
                await Task.Delay(
                    TimeSpan.FromMilliseconds(500 * (attempt + 1)),
                    cancellationToken);
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
                        ? existing + length
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
        if (total is not null && new FileInfo(part).Length != total.Value)
        {
            throw new IOException("下载不完整，断点已保留");
        }
        File.Move(part, destination, overwrite: true);
        progress?.Report((candidate.Label, received, total ?? received, 0));
    }

    private static async Task<(bool SupportsRange, long? Total)> ProbeRangeAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
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
        mirror.Contains("ghfast", StringComparison.OrdinalIgnoreCase) ? "Fast 加速" :
        mirror.Contains("gh-proxy", StringComparison.OrdinalIgnoreCase) ? "Proxy 加速" :
        "Mirror 加速";

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
