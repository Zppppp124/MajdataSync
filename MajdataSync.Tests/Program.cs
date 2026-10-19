using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataSync;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "MajdataSyncTests");
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }

            Directory.CreateDirectory(testRoot);
            var original = Path.Combine(testRoot, "MaiCharts", "Original");
            Directory.CreateDirectory(original);
            File.WriteAllText(Path.Combine(testRoot, "MajdataPlay.exe"), "");

            var detected = FolderDetector.DetectNearby(Path.Combine(testRoot, "MajdataPlay.exe"));
            Assert(detected is not null, "FolderDetector should find MaiCharts\\Original near MajdataPlay.exe");
            Assert(detected!.RootPath == original, "FolderDetector returned wrong Original path");

            var nestedZip = Path.Combine(testRoot, "Nested Song.zip");
            CreateNestedZip(nestedZip);
            using var api = new ChartApi();
            var installer = new ChartInstaller(api);
            var record = await installer.ImportZipAsync(detected, nestedZip, null, CancellationToken.None);
            var expectedFolder = Path.Combine(original, "Nested Song");
            Assert(Directory.Exists(expectedFolder), "ZIP should create a folder named after the ZIP");
            Assert(File.Exists(Path.Combine(expectedFolder, "maidata.txt")), "maidata.txt should be at chart root");
            Assert(File.Exists(Path.Combine(expectedFolder, "track.mp3")), "track.mp3 should be at chart root");
            Assert(File.Exists(Path.Combine(expectedFolder, "bg.jpg")), "bg.jpg should be at chart root");
            Assert(record.Folder == "Nested Song", "Import record contains wrong folder name");

            var escapedZip = Path.Combine(testRoot, "Malicious.zip");
            CreateUnsafeZip(escapedZip);
            var blocked = false;
            try
            {
                await installer.ImportZipAsync(detected, escapedZip, null, CancellationToken.None);
            }
            catch (InvalidDataException)
            {
                blocked = true;
            }

            Assert(blocked, "ZIP path traversal should be rejected");
            var crossVolumeZip = Path.Combine(Path.GetTempPath(), $"MajdataSyncCrossVolume-{Guid.NewGuid():N}.zip");
            CreateNestedZip(crossVolumeZip);
            try
            {
                var crossVolumeRecord = await installer.ImportZipAsync(
                    detected,
                    crossVolumeZip,
                    null,
                    CancellationToken.None);
                var crossVolumeFolder = Path.Combine(original, crossVolumeRecord.Folder);
                Assert(File.Exists(Path.Combine(crossVolumeFolder, "maidata.txt")),
                    "Cross-volume ZIP import should commit successfully");
                Assert(!Directory.EnumerateFileSystemEntries(original, ".*majdata-staging*").Any(),
                    "Cross-volume staging folders should be cleaned up");
            }
            finally
            {
                if (File.Exists(crossVolumeZip))
                {
                    File.Delete(crossVolumeZip);
                }
            }

            Assert(FolderDetector.SafeFolderName("CON") == "CON_", "Windows reserved folder name should be escaped");

            if (args.Length == 2 &&
                !args[0].StartsWith("download:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("catalog:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("network:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("speed:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("routes:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("cache:", StringComparison.OrdinalIgnoreCase))
            {
                var sampleRoot = FolderDetector.DetectFrom(args[0]);
                Assert(sampleRoot is not null, "Sample root should be detected as a Majdata folder");
                var sampleRecord = await installer.ImportZipAsync(
                    sampleRoot!, args[1], null, CancellationToken.None);
                var sampleFiles = new[] { "maidata.txt", "track.mp3", "bg.jpg", "pv.mp4" };
                foreach (var file in sampleFiles)
                {
                    Assert(File.Exists(Path.Combine(sampleRoot!.RootPath, sampleRecord.Folder, file)),
                        "Sample ZIP is missing " + file);
                }

                Console.WriteLine("PASS: supplied ZIP imported with flat Majdata layout");
            }

            if (args.Length == 2 && args[0].StartsWith("download:", StringComparison.OrdinalIgnoreCase))
            {
                var downloadRootPath = args[0]["download:".Length..];
                var downloadRoot = FolderDetector.DetectFrom(downloadRootPath);
                Assert(downloadRoot is not null, "Download test root should be detected");
                var chart = new ChartItem
                {
                    Id = args[1],
                    Title = "Sparkle New Life",
                    Artist = "rider",
                    Hash = "test-hash",
                    Timestamp = DateTimeOffset.Now
                };
                var liveRecord = await installer.DownloadChartAsync(
                    downloadRoot!,
                    chart,
                    includeVideo: false,
                    concurrency: 4,
                    (label, ratio) =>
                    {
                        Console.WriteLine($"{label} ratio={ratio:F2}");
                        return Task.CompletedTask;
                    },
                    CancellationToken.None);
                Assert(liveRecord is not null, "Download record should be returned");
                var downloadedFolder = Path.Combine(downloadRoot!.RootPath, liveRecord!.Folder);
                foreach (var file in new[] { "maidata.txt", "track.mp3", "bg.jpg", "id" })
                {
                    Assert(File.Exists(Path.Combine(downloadedFolder, file)),
                        "Downloaded chart is missing " + file);
                    Assert(new FileInfo(Path.Combine(downloadedFolder, file)).Length > 0,
                        "Downloaded chart file is empty: " + file);
                }

                Console.WriteLine("PASS: live chart resource download and flat layout");
            }

            if (args.Length == 2 && args[0].StartsWith("catalog:", StringComparison.OrdinalIgnoreCase))
            {
                var catalogRoot = FolderDetector.DetectFrom(args[0]["catalog:".Length..]);
                Assert(catalogRoot is not null, "Catalog test root should be detected");
                var controller = new AppController(null);
                var count = await controller.LoadIndexCountForTestAsync(CancellationToken.None);
                Assert(count > 0, "Live catalog should contain charts");
                var pageSize = 24;
                var pageCount = (int)Math.Ceiling(count / (double)pageSize);
                Assert(pageCount > 1, "Live catalog should require pagination");
                Console.WriteLine($"PASS: live catalog index has {count} charts across {pageCount} pages");
                controller.Dispose();
            }

            if (args.Length == 2 && args[0].StartsWith("network:", StringComparison.OrdinalIgnoreCase))
            {
                using var networkApi = new ChartApi();
                var routes = await networkApi.RefreshNetworkNodesAsync(CancellationToken.None);
                var node = routes.FirstOrDefault(route => route.Available)?.Addresses.FirstOrDefault()?.ToString();
                Assert(!string.IsNullOrWhiteSpace(node), "Network optimizer should select a CDN node");
                var output = Path.Combine(args[0]["network:".Length..], "live-track.mp3");
                var host = routes.FirstOrDefault(route => route.Available)?.Host ?? "majdata.net";
                await networkApi.DownloadFileAsync(
                    ChartApi.ResourceUrl(host, args[1], "track"),
                    output,
                    null,
                    CancellationToken.None,
                    segmentCount: 2);
                Assert(File.Exists(output), "Segmented download should create the destination file");
                var length = new FileInfo(output).Length;
                Assert(length > 1_000_000, "Segmented download should produce a full track");
                Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(output)!, "*.part*").Any(),
                    "Segmented temporary files should be cleaned up");
                Console.WriteLine($"PASS: segmented download via {node}, {length} bytes");
            }

            if (args.Length == 2 && args[0].StartsWith("speed:", StringComparison.OrdinalIgnoreCase))
            {
                using var speedApi = new ChartApi();
                var directory = args[0]["speed:".Length..];
                Directory.CreateDirectory(directory);
                var singleOutput = Path.Combine(directory, "single.mp3");
                var segmentedOutput = Path.Combine(directory, "segmented.mp3");
                var singleWatch = System.Diagnostics.Stopwatch.StartNew();
                await speedApi.DownloadFileAsync(
                    ChartApi.Track(args[1]),
                    singleOutput,
                    null,
                    CancellationToken.None,
                    segmentCount: 1);
                singleWatch.Stop();
                var segmentedWatch = System.Diagnostics.Stopwatch.StartNew();
                await speedApi.DownloadFileAsync(
                    ChartApi.Track(args[1]),
                    segmentedOutput,
                    null,
                    CancellationToken.None,
                    segmentCount: 2);
                segmentedWatch.Stop();
                Assert(new FileInfo(singleOutput).Length == new FileInfo(segmentedOutput).Length,
                    "Single and segmented downloads should produce identical sizes");
                Console.WriteLine(
                    $"PASS: single={singleWatch.ElapsedMilliseconds}ms segmented={segmentedWatch.ElapsedMilliseconds}ms");
            }

            if (args.Length == 2 && args[0].StartsWith("routes:", StringComparison.OrdinalIgnoreCase))
            {
                using var routeApi = new ChartApi();
                var routes = await routeApi.RefreshNetworkNodesAsync(CancellationToken.None);
                Assert(routes.Any(route => route.Available), "At least one network route should be available");
                foreach (var route in routes)
                {
                    var latency = route.LatencyMs == long.MaxValue
                        ? "unavailable"
                        : route.LatencyMs + "ms";
                    Console.WriteLine(
                        $"{route.Host} latency={latency} addresses={string.Join(',', route.Addresses)}");
                }

                Console.WriteLine("PASS: site and IP routing");
            }

            if (args.Length == 2 && args[0].StartsWith("cache:", StringComparison.OrdinalIgnoreCase))
            {
                var cacheRoot = FolderDetector.DetectFrom(args[0]["cache:".Length..]);
                Assert(cacheRoot is not null, "Cache test root should be detected");
                var cacheChart = new ChartItem
                {
                    Id = "b0fb0807-ccf8-44a1-8649-573024cd4e04",
                    Title = "Sparkle New Life",
                    Artist = "rider",
                    Hash = "geVdAhVCF24HX6nlw+G0qA==",
                    Timestamp = DateTimeOffset.Now
                };
                using var cacheApi = new ChartApi();
                var cacheInstaller = new ChartInstaller(cacheApi);
                var sourceRecord = await cacheInstaller.DownloadChartAsync(
                    cacheRoot!,
                    cacheChart,
                    includeVideo: false,
                    concurrency: 4,
                    (_, _) => Task.CompletedTask,
                    CancellationToken.None);
                Assert(sourceRecord is not null, "Cache source download should succeed");
                await OnlineCacheWriter.PublishAsync(
                    cacheRoot!,
                    cacheChart,
                    Path.Combine(cacheRoot!.RootPath, sourceRecord!.Folder),
                    CancellationToken.None);
                var expectedCache = OnlineCacheWriter.CacheFolder(cacheRoot!, cacheChart.Hash);
                Assert(Directory.Exists(expectedCache), "Online cache folder should exist");
                foreach (var file in new[] { "maidata.txt", "track.mp3", "bg.jpg", "bg_fullSize.jpg" })
                {
                    var path = Path.Combine(expectedCache, file);
                    Assert(File.Exists(path), "Online cache is missing " + file);
                    var expected = (await File.ReadAllTextAsync(path + ".sha256")).Trim();
                    await using var stream = File.OpenRead(path);
                    var actual = Convert.ToBase64String(await SHA256.HashDataAsync(stream));
                    Assert(expected == actual, "Online cache hash mismatch: " + file);
                }

                Console.WriteLine("PASS: MajdataPlay online cache layout and Base64 SHA-256");
            }

                Console.WriteLine("PASS: directory detection, cross-volume import, and traversal protection");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void CreateNestedZip(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "Song Folder\\maidata.txt", "&title=Nested Song\n&artist=Test");
        Add(archive, "Song Folder\\track.mp3", "audio");
        Add(archive, "Song Folder\\bg.jpg", "image");
    }

    private static void CreateUnsafeZip(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "../outside.txt", "bad");
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
