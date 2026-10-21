using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace MajdataSync;

internal sealed class GitHubAcceleratedDownloader
{
    private const int SegmentCount = 12;
    internal static readonly string[] Mirrors =
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
        var candidates = CandidateUrls(url)
            .Select(candidate => (
                Label: candidate.Equals(url, StringComparison.Ordinal)
                    ? "直连 GitHub"
                    : MirrorLabel(Mirrors.First(mirror =>
                        candidate.StartsWith(mirror, StringComparison.Ordinal))),
                Url: candidate))
            .ToList();

        Exception? lastError = null;
        for (var round = 0; round < 4; round++)
        {
            try
            {
                await DownloadResumableAsync(
                    destination,
                    candidates,
                    progress,
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, round + 1)), cancellationToken);
            }
        }

        throw new IOException("GitHub 加速线路均下载失败", lastError);
    }

    public Task<List<string>> CreateRankedCandidatesAsync(
        string url,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CandidateUrls(url).ToList());
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
        var tasks = probes.Select(probe => ProbeAsync(
            http,
            probe.Label,
            probe.Url,
            cancellationToken));
        var results = await Task.WhenAll(tasks);
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
        probeCancellation.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 65535);
            var stopwatch = Stopwatch.StartNew();
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                probeCancellation.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(
                probeCancellation.Token);
            var buffer = new byte[16 * 1024];
            var total = 0;
            while (total < 64 * 1024)
            {
                var read = await stream.ReadAsync(buffer, probeCancellation.Token);
                if (read == 0)
                {
                    break;
                }
                total += read;
            }
            stopwatch.Stop();
            return total >= 1024
                ? (label, url, total / Math.Max(0.01, stopwatch.Elapsed.TotalSeconds))
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
        IReadOnlyList<(string Label, string Url)> candidates,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        using var http = CreateClient();
        long? total = null;
        foreach (var (label, url) in candidates.Where(candidate =>
                     !candidate.Label.Equals("直连 GitHub", StringComparison.Ordinal)))
        {
            total = await GetContentLengthAsync(http, url, cancellationToken);
            if (total is > 0)
            {
                break;
            }
        }
        if (total is null or < 1_000_000)
        {
            await DownloadSingleAcrossSourcesAsync(
                http,
                destination,
                candidates,
                progress,
                cancellationToken);
            return;
        }

        var ranges = BuildRanges(total.Value, SegmentCount);
        var segments = ranges
            .Select((range, index) => new Segment(
                index,
                range.Start,
                range.End,
                destination + $".segment{index}"))
            .ToArray();
        foreach (var segment in segments)
        {
            if (File.Exists(segment.Path))
            {
                var length = new FileInfo(segment.Path).Length;
                if (length > segment.Expected)
                {
                    using var truncate = new FileStream(segment.Path, FileMode.Open, FileAccess.Write);
                    truncate.SetLength(segment.Expected);
                    length = segment.Expected;
                }
                segment.Downloaded = length;
            }
        }

        var received = new long[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            received[index] = segments[index].Downloaded;
        }
        var baseBytes = received.Sum();
        var speedBytes = 0L;
        var speedWatch = Stopwatch.StartNew();
        var candidateIndex = 0;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Downloaded >= segment.Expected)
            {
                continue;
            }

            var reserved = candidateIndex++;
            var primaryCount = Math.Min(3, candidates.Count);
            var candidate = candidates[reserved % primaryCount];
            using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            stallCancellation.CancelAfter(TimeSpan.FromSeconds(20));
            progress?.Report((candidate.Label, baseBytes, total, 0));
            await DownloadSegmentWithFailoverAsync(
                http,
                segment,
                candidates,
                reserved % primaryCount,
                current =>
                {
                    received[segment.Index] = current;
                    var aggregate = received.Sum();
                    if (speedWatch.ElapsedMilliseconds >= 250)
                    {
                        lock (received)
                        {
                            if (speedWatch.ElapsedMilliseconds >= 250)
                            {
                                var speed = (aggregate - baseBytes - speedBytes) /
                                            Math.Max(0.001, speedWatch.Elapsed.TotalSeconds);
                                speedBytes = aggregate - baseBytes;
                                speedWatch.Restart();
                                progress?.Report((candidate.Label, aggregate, total, speed));
                            }
                        }
                    }
                    stallCancellation.CancelAfter(TimeSpan.FromSeconds(20));
                },
                stallCancellation.Token);
        }

        foreach (var segment in segments)
        {
            if (new FileInfo(segment.Path).Length != segment.Expected)
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
        progress?.Report(("已完成", total.Value, total, 0));
    }

    private static async Task DownloadSegmentWithFailoverAsync(
        HttpClient http,
        Segment segment,
        IReadOnlyList<(string Label, string Url)> candidates,
        int initialCandidate,
        Action<long> updateProgress,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < candidates.Count * 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = segment.Downloaded;
            if (offset >= segment.Expected)
            {
                return;
            }

            var candidate = candidates[(initialCandidate + attempt) % candidates.Count];
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
                    var wanted = (int)Math.Min(buffer.Length, segment.Expected - offset);
                    var read = await source.ReadAsync(
                        buffer.AsMemory(0, wanted),
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    offset += read;
                    segment.Downloaded = offset;
                    updateProgress(offset);
                }
                await output.FlushAsync(cancellationToken);
                if (offset >= segment.Expected)
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
                // Keep completed bytes in this segment and try the next route.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 350 * (attempt + 1))), cancellationToken);
        }

        throw new IOException($"分片 {segment.Index + 1} 无法完成，已保留断点");
    }

    private static async Task<long?> GetContentLengthAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
            !Mirrors.Any(mirror => url.StartsWith(mirror, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        try
        {
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return response.StatusCode == HttpStatusCode.PartialContent
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DownloadSingleAcrossSourcesAsync(
        HttpClient http,
        string destination,
        IReadOnlyList<(string Label, string Url)> candidates,
        IProgress<(string Source, long Bytes, long? Total, double Speed)>? progress,
        CancellationToken cancellationToken)
    {
        var part = destination + ".part";
        foreach (var (label, url) in candidates)
        {
            try
            {
                var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
                    if (speedWatch.ElapsedMilliseconds >= 250)
                    {
                        progress?.Report((
                            label,
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
                    throw new IOException("下载不完整，保留断点并切换线路");
                }
                File.Move(part, destination, overwrite: true);
                progress?.Report((label, received, total ?? received, 0));
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // The .part file is intentionally retained for the next route.
            }
        }
        throw new IOException("GitHub 加速线路均下载失败");
    }

    private static List<(long Start, long End)> BuildRanges(long total, int count)
    {
        var result = new List<(long Start, long End)>();
        var size = total / count;
        for (var index = 0; index < count; index++)
        {
            var start = index * size;
            var end = index == count - 1 ? total - 1 : start + size - 1;
            result.Add((start, end));
        }
        return result;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            MaxConnectionsPerServer = 12,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 MajdataSync/1.8");
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
