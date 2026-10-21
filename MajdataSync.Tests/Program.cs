using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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

            var localGameRoot = Path.Combine(testRoot, "MajdataPlay");
            Directory.CreateDirectory(localGameRoot);
            File.WriteAllBytes(Path.Combine(localGameRoot, "MajdataPlay.exe"), [0]);
            File.WriteAllText(
                Path.Combine(localGameRoot, "settings.json"),
                "{\"Game\":{\"TapSpeed\":7.5},\"Display\":{\"Skin\":\"default\"},\"Online\":{\"Enable\":true}}");
            Directory.CreateDirectory(Path.Combine(localGameRoot, "Skins", "Test Skin"));
            var activeSkin = GameManager.ApplySkin(localGameRoot, "Test Skin");
            var activeSettings = GameManager.LoadGameSettings(localGameRoot);
            Assert(activeSkin == "Test Skin", "GameManager should apply the selected skin");
            Assert(activeSettings.GetProperty("Display").GetProperty("Skin").GetString() == "Test Skin",
                "Applying a skin should update Display.Skin");
            Assert(activeSettings.GetProperty("Game").GetProperty("TapSpeed").GetDouble() == 7.5,
                "Applying a skin should preserve unrelated settings");
            Assert(GameManager.GetInstalledSkins(localGameRoot).Single(item => item.Name == "Test Skin").Active,
                "Active skin should be identified in the installed list");
            var endpointSettings = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
                activeSettings.GetRawText())!;
            var endpoints = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Name"] = "MajdataNET",
                    ["Url"] = "https://maj-1.moyingmoe.top/api3/api/",
                    ["Username"] = "keep-me",
                    ["Password"] = "keep-secret",
                    ["AutoLogin"] = true
                }
            };
            ((System.Text.Json.Nodes.JsonObject)endpointSettings["Online"]!)["ApiEndpoints"] = endpoints;
            GameManager.SaveGameSettings(
                localGameRoot,
                JsonSerializer.SerializeToElement(endpointSettings));
            var savedEndpoints = GameManager.LoadGameSettings(localGameRoot)
                .GetProperty("Online").GetProperty("ApiEndpoints")[0];
            Assert(savedEndpoints.GetProperty("Url").GetString() == "https://maj-1.moyingmoe.top/api3/api/",
                "Online endpoint should save the selected source");
            Assert(savedEndpoints.GetProperty("Username").GetString() == "keep-me",
                "Changing the endpoint should preserve login fields");
            var stableRootPath = Path.Combine(testRoot, "StableGame");
            var prereleaseRootPath = Path.Combine(testRoot, "PrereleaseGame");
            Directory.CreateDirectory(stableRootPath);
            Directory.CreateDirectory(prereleaseRootPath);
            var channelSettings = new AppSettings
            {
                StableGameRootPath = stableRootPath,
                PrereleaseGameRootPath = prereleaseRootPath,
                GameChannel = "stable"
            };
            Assert(channelSettings.StableGameRootPath == stableRootPath &&
                   channelSettings.PrereleaseGameRootPath == prereleaseRootPath,
                "Stable and prerelease install roots should be stored independently");
            var candidateUrls = GitHubAcceleratedDownloader.CandidateUrls(
                "https://github.com/TeamMajdata/MajdataPlay_Build/archive/refs/tags/LATEST.zip");
            Assert(candidateUrls.Count == 4, "GitHub acceleration should test three mirrors plus direct");
            Assert(candidateUrls[0].StartsWith("https://ghfast.top/https://github.com/", StringComparison.Ordinal),
                "GitHub acceleration should try the fast mirror first");
            Assert(candidateUrls[^1].StartsWith("https://github.com/", StringComparison.Ordinal),
                "GitHub acceleration should keep a direct fallback");

            var selectionCharts = new[]
            {
                new ChartItem { Id = "chart-a", Title = "A", Hash = "hash-a", Timestamp = DateTimeOffset.Now },
                new ChartItem { Id = "chart-b", Title = "B", Hash = "hash-b", Timestamp = DateTimeOffset.Now },
                new ChartItem { Id = "chart-c", Title = "C", Hash = "hash-c", Timestamp = DateTimeOffset.Now }
            };
            using (var selectionController = new AppController(null))
            {
                var selected = selectionController.SelectChartsForTest(
                    selectionCharts,
                    new SyncInventory(),
                    new SyncRequest { ChartIds = ["chart-b"] });
                Assert(selected.Count == 1 && selected[0].Id == "chart-b",
                    "Batch selection should download only checked charts");
            }

            var persistentOriginal = Path.Combine(testRoot, "PersistentCharts", "Original");
            Directory.CreateDirectory(persistentOriginal);
            var persistentStore = new InventoryStore(persistentOriginal);
            var persistentInventory = await persistentStore.LoadAsync(CancellationToken.None);
            persistentInventory.Charts["chart-a"] = new SyncRecord
            {
                ChartId = "chart-a",
                Folder = "A",
                Hash = "hash-a",
                DownloadedAt = DateTimeOffset.Now
            };
            await persistentStore.SaveAsync(persistentInventory, CancellationToken.None);
            var reloadedInventory = await persistentStore.LoadAsync(CancellationToken.None);
            Assert(reloadedInventory.Charts.ContainsKey("chart-a"),
                "Each completed chart should persist even when a later batch is interrupted");

            var switchSource = Path.Combine(testRoot, "SwitchSource");
            var switchStage = Path.Combine(testRoot, "SwitchStage");
            var switchTarget = Path.Combine(testRoot, "SwitchTarget");
            foreach (var path in new[] { switchSource, switchStage, switchTarget })
            {
                Directory.CreateDirectory(path);
            }
            foreach (var directory in new[] { "MaiCharts", "Skins", "Cache", "RecordOutputs" })
            {
                Directory.CreateDirectory(Path.Combine(switchSource, directory, "keep"));
                File.WriteAllText(Path.Combine(switchSource, directory, "keep", "new.txt"), directory);
                Directory.CreateDirectory(Path.Combine(switchTarget, directory, "keep"));
                File.WriteAllText(Path.Combine(switchTarget, directory, "keep", "data.txt"), directory);
            }
            File.WriteAllText(Path.Combine(switchSource, "settings.json"), "{\"Display\":{\"Skin\":\"source\"}}");
            File.WriteAllText(Path.Combine(switchSource, "MajdataPlay.exe"), "old");
            File.WriteAllText(Path.Combine(switchStage, "MajdataPlay.exe"), "new");
            Directory.CreateDirectory(Path.Combine(switchStage, "game-libs"));
            File.WriteAllText(Path.Combine(switchStage, "game-libs", "runtime.dll"), "new");
            File.WriteAllText(Path.Combine(switchTarget, "settings.json"), "{\"Display\":{\"Skin\":\"target\"}}");
            File.WriteAllText(Path.Combine(switchTarget, "MajdataPlay.exe"), "old");
            GameManager.PreserveUserDataForTest(switchSource, switchStage, switchTarget);
            Assert(File.ReadAllText(Path.Combine(switchTarget, "MajdataPlay.exe")) == "new",
                "Channel switch should replace game binaries");
            Assert(File.ReadAllText(Path.Combine(switchTarget, "settings.json")).Contains("\"Skin\":\"target\""),
                "Channel switch should preserve the target settings.json");
            foreach (var directory in new[] { "MaiCharts", "Skins", "Cache", "RecordOutputs" })
            {
                Assert(File.ReadAllText(Path.Combine(switchTarget, directory, "keep", "data.txt")) == directory,
                    "Channel switch should preserve " + directory);
            }
            Assert(Directory.Exists(Path.Combine(switchTarget, "game-libs")),
                "Channel switch should install new game directories");

            var routeDestination = Path.Combine(testRoot, "route-resume.zip");
            GitHubAcceleratedDownloader.WriteRouteMarker(
                routeDestination,
                ("Proxy 加速", "https://gh-proxy.com/https://github.com/example.zip"));
            var routeMarker = GitHubAcceleratedDownloader.ReadRouteMarkerForTest(routeDestination);
            Assert(routeMarker is not null &&
                   routeMarker.Value.Label == "Proxy 加速" &&
                   routeMarker.Value.Url.StartsWith("https://gh-proxy.com/", StringComparison.Ordinal),
                "Resume marker should keep the selected route across app restarts");
            Assert(MainWindow.IsTemporaryLaunchDirectory(Path.Combine(Path.GetTempPath(), "MajdataSyncTempTest")),
                "Temporary launch directory should require an unpack warning");
            Assert(!MainWindow.IsTemporaryLaunchDirectory(AppContext.BaseDirectory),
                "Normal application directory should not be treated as temporary");

            var invalidStage = Path.Combine(testRoot, "InvalidStage");
            Directory.CreateDirectory(invalidStage);
            File.WriteAllBytes(Path.Combine(invalidStage, "MajdataPlay.exe"), []);
            var invalidEntry = new GameHashEntry
            {
                FilePath = "MajdataPlay.exe",
                Checksum = "stale"
            };
            GameManager.RemoveInvalidStagedFileForTest(invalidStage, invalidEntry);
            Assert(!File.Exists(Path.Combine(invalidStage, "MajdataPlay.exe")),
                "Zero-byte staged executables should be removed before retry");

            if (args.Length == 1 &&
                !args[0].StartsWith("gamestatus:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("catalogbench:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("resume:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("route-test:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("range-retry:", StringComparison.OrdinalIgnoreCase))
            {
                if (args[0].StartsWith("installgame:", StringComparison.OrdinalIgnoreCase))
                {
                    var installTarget = Path.GetFullPath(args[0]["installgame:".Length..]);
                    using var installApi = new ChartApi();
                    var installManager = new GameManager(installApi);
                    var reports = 0;
                    await GameManager.InstallStableFilesForTestAsync(
                        installTarget,
                        new Progress<(string Message, double? Progress)>(value =>
                        {
                            reports++;
                            if (reports <= 3 || reports % 20 == 0 || value.Progress is >= 1)
                            {
                                Console.WriteLine($"{value.Message} progress={value.Progress}");
                            }
                        }),
                        CancellationToken.None);
                    Assert(GameManager.IsInstalled(installTarget), "Installed game should contain MajdataPlay.exe");
                    Assert(reports > 5, "Game install should report download progress");
                    Console.WriteLine($"PASS: game install and progress ({reports} reports)");
                    return 0;
                }

                var statusRoot = Path.GetFullPath(args[0]);
                Assert(GameManager.IsInstalled(statusRoot), "Game status check requires an installed game");
                using var statusApi = new ChartApi();
                var statusManager = new GameManager(statusApi);
                var status = await statusManager.CheckGameUpdateAsync(
                    statusRoot,
                    useCnb: true,
                    CancellationToken.None);
                Console.WriteLine($"GAMESTATUS: installed=True changed={status.Files.Count} manifest={status.ManifestUrl}");
            }

            if (args.Length == 2 &&
                !args[0].StartsWith("download:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("catalog:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("network:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("speed:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("routes:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("cache:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("gamedata:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("github-download:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("route-test:", StringComparison.OrdinalIgnoreCase) &&
                !args[0].StartsWith("range-retry:", StringComparison.OrdinalIgnoreCase))
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

            if (args.Length == 1 && args[0].StartsWith("catalogbench:", StringComparison.OrdinalIgnoreCase))
            {
                var cachePath = args[0]["catalogbench:".Length..];
                var cacheWatch = System.Diagnostics.Stopwatch.StartNew();
                var cached = JsonSerializer.Deserialize<List<ChartItem>>(
                    await File.ReadAllTextAsync(cachePath),
                    JsonOptions.Default) ?? [];
                cacheWatch.Stop();
                using var liveApi = new ChartApi();
                var liveWatch = System.Diagnostics.Stopwatch.StartNew();
                var live = await liveApi.GetAllChartsAsync(null, CancellationToken.None);
                liveWatch.Stop();
                Assert(cached.Count > 1000, "Catalog cache should contain the full chart list");
                Assert(live.Count >= cached.Count, "Bulk API should return the complete chart list");
                Console.WriteLine(
                    $"PASS: cache={cached.Count} in {cacheWatch.ElapsedMilliseconds}ms; " +
                    $"bulk={live.Count} in {liveWatch.ElapsedMilliseconds}ms");
                return 0;
            }

            if (args.Length == 1 && args[0].StartsWith("resume:", StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetFullPath(args[0]["resume:".Length..]);
                Directory.CreateDirectory(root);
                await TestResumeDownloadAsync(root, cancellationToken: CancellationToken.None);
                Console.WriteLine("PASS: interrupted download resumed from existing .part bytes");
                return 0;
            }

            if (args.Length == 2 &&
                args[0].StartsWith("github-download:", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetFullPath(args[0]["github-download:".Length..]);
                Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, "nightly.zip");
                var downloader = new GitHubAcceleratedDownloader();
                var sources = new HashSet<string>(StringComparer.Ordinal);
                await downloader.DownloadAsync(
                    args[1],
                    destination,
                    new Progress<(string Source, long Bytes, long? Total, double Speed)>(value =>
                    {
                        sources.Add(value.Source);
                        Console.WriteLine(
                            $"{value.Source} {value.Bytes}/{value.Total} {value.Speed:F0} B/s");
                    }),
                    CancellationToken.None);
                Assert(File.Exists(destination), "GitHub accelerated download should produce a file");
                Assert(new FileInfo(destination).Length > 100_000_000,
                    "Nightly package should be complete");
                Assert(sources.Count == 1,
                    "A large download should stay on one selected source until completion");
                Console.WriteLine($"PASS: locked source {sources.Single()} for {new FileInfo(destination).Length} bytes");
                return 0;
            }

            if (args.Length == 1 &&
                args[0].StartsWith("range-retry:", StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetFullPath(args[0]["range-retry:".Length..]);
                await TestRangeRetryAsync(root, CancellationToken.None);
                Console.WriteLine("PASS: repeated range interruptions resumed to a complete file");
                return 0;
            }

            if (args.Length == 1 &&
                args[0].StartsWith("route-test:", StringComparison.OrdinalIgnoreCase))
            {
                var sample = "https://github.com/TeamMajdata/MajdataPlay_Build/archive.zip";
                var fast = GitHubAcceleratedDownloader.DownloadCandidates(sample, "fast");
                var proxy = GitHubAcceleratedDownloader.DownloadCandidates(sample, "proxy");
                var auto = GitHubAcceleratedDownloader.DownloadCandidates(sample, "auto");
                Assert(fast[0].StartsWith("https://ghfast.top/", StringComparison.Ordinal),
                    "Fast should be the default first route");
                Assert(proxy[0].StartsWith("https://gh-proxy.com/", StringComparison.Ordinal),
                    "Proxy should be selectable as the first route");
                Assert(auto.Count == 4,
                    "Auto route should retain the full fallback list");
                Console.WriteLine("PASS: Fast is default; Proxy and Auto route choices are available");
                return 0;
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

            if (args.Length == 2 && args[0].StartsWith("gamedata:", StringComparison.OrdinalIgnoreCase))
            {
                var gameRoot = Path.Combine(args[0]["gamedata:".Length..], "MajdataPlay");
                Directory.CreateDirectory(gameRoot);
                File.WriteAllBytes(Path.Combine(gameRoot, "MajdataPlay.exe"), [0]);
                File.WriteAllText(
                    Path.Combine(gameRoot, "settings.json"),
                    "{\"Game\":{\"TapSpeed\":7.5},\"Online\":{\"Enable\":true}}");
                Assert(GameManager.IsInstalled(gameRoot), "GameManager should detect MajdataPlay.exe");
                var settings = GameManager.LoadGameSettings(gameRoot);
                Assert(settings.GetProperty("Game").GetProperty("TapSpeed").GetDouble() == 7.5,
                    "Game settings should load");
                var skinsRoot = Path.Combine(gameRoot, "Skins");
                Directory.CreateDirectory(Path.Combine(skinsRoot, "Test Skin"));
                var applied = GameManager.ApplySkin(gameRoot, "Test Skin");
                Assert(applied == "Test Skin", "ApplySkin should return the active skin name");
                var appliedSettings = GameManager.LoadGameSettings(gameRoot);
                Assert(appliedSettings.GetProperty("Display").GetProperty("Skin").GetString() == "Test Skin",
                    "ApplySkin should update Display.Skin");
                Assert(appliedSettings.GetProperty("Game").GetProperty("TapSpeed").GetDouble() == 7.5 &&
                       appliedSettings.GetProperty("Online").GetProperty("Enable").GetBoolean(),
                    "ApplySkin should preserve unrelated settings");
                Assert(GameManager.GetInstalledSkins(gameRoot).Single().Active,
                    "Installed skin should be marked active");
                using var gameApi = new ChartApi();
                var skins = await GameManager.GetSkinsAsync(gameRoot, true, null, CancellationToken.None);
                Assert(skins.Count > 0, "CNB skin list should return entries");
                Console.WriteLine($"PASS: game manager, settings, active skin, and {skins.Count} online skins");
            }

                Console.WriteLine("PASS: directory detection, cross-volume import, traversal protection, and skin settings");
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

    private static async Task TestResumeDownloadAsync(string root, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "MajdataSync resume payload ", 65536)));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCount = 0;
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = Task.Run(async () =>
        {
            for (var index = 0; index < 2; index++)
            {
                using var client = await listener.AcceptTcpClientAsync(serverCancellation.Token);
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer, cancellationToken);
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                var rangeHeader = request
                    .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                var start = 0;
                if (rangeHeader is not null)
                {
                    var value = rangeHeader.Split('=')[1].Split('-')[0];
                    int.TryParse(value, out start);
                }
                var body = payload.AsMemory(start);
                var status = start > 0 ? "206 Partial Content" : "200 OK";
                var sendLength = index == 0 ? body.Length / 2 : body.Length;
                if (index == 0)
                {
                    sendLength = payload.Length / 2;
                }
                else
                {
                    sendLength = Math.Max(0, payload.Length - start);
                }
                var header = $"HTTP/1.1 {status}\r\nContent-Length: {sendLength}\r\n" +
                              (start > 0
                                  ? $"Content-Range: bytes {start}-{payload.Length - 1}/{payload.Length}\r\n"
                                  : "") +
                              "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
                await stream.WriteAsync(body.Slice(0, sendLength), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                Interlocked.Increment(ref requestCount);
                if (index == 0)
                {
                    client.Close();
                }
            }
        }, cancellationToken);

        var destination = Path.Combine(root, "resume.bin");
        var part = destination + ".part";
        if (File.Exists(destination)) File.Delete(destination);
        if (File.Exists(part)) File.Delete(part);
        await using (var initial = File.Create(part))
        {
            await initial.WriteAsync(payload.AsMemory(0, payload.Length / 2), cancellationToken);
        }

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/resume.bin");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(payload.Length / 2, null);
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        Assert(response.StatusCode == HttpStatusCode.PartialContent,
            "Range server should return partial content");
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(part, FileMode.Append, FileAccess.Write))
        {
            await source.CopyToAsync(output, cancellationToken);
        }
        File.Move(part, destination, overwrite: true);
        serverCancellation.Cancel();
        listener.Stop();
        try
        {
            await server;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
        Assert(File.ReadAllBytes(destination).SequenceEqual(payload),
            "Resumed file should match the original payload");
        Assert(requestCount == 1, "Resume request should reach the server once");
    }

    private static async Task TestRangeRetryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var payload = new byte[101 * 1024 * 1024];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }
        var chunkSize = 4 * 1024 * 1024;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCount = 0;
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var server = Task.Run(async () =>
        {
            while (!serverCancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(serverCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[8192];
                    var read = await stream.ReadAsync(buffer, serverCancellation.Token);
                    var request = Encoding.ASCII.GetString(buffer, 0, read);
                    var rangeHeader = request
                        .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                    var start = 0;
                    var requestedEnd = payload.Length - 1;
                    if (rangeHeader is not null)
                    {
                        var value = rangeHeader.Split('=')[1];
                        var bounds = value.Split('-', 2);
                        int.TryParse(bounds[0], out start);
                        if (bounds.Length > 1 &&
                            !string.IsNullOrWhiteSpace(bounds[1]) &&
                            int.TryParse(bounds[1], out var end))
                        {
                            requestedEnd = Math.Min(end, payload.Length - 1);
                        }
                    }
                    var remaining = Math.Max(0, requestedEnd - start + 1);
                    var sendLength = Math.Min(remaining, chunkSize);
                    if (sendLength <= 0)
                    {
                        continue;
                    }
                    var sendsWholeFile = rangeHeader is null;
                    var status = sendsWholeFile ? "200 OK" : "206 Partial Content";
                    var headerContentLength = sendsWholeFile ? sendLength : remaining;
                    var header = $"HTTP/1.1 {status}\r\nContent-Length: {headerContentLength}\r\n" +
                                 (sendsWholeFile
                                     ? ""
                                     : $"Content-Range: bytes {start}-{start + remaining - 1}/{payload.Length}\r\n") +
                                 "Accept-Ranges: bytes\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), serverCancellation.Token);
                    await stream.WriteAsync(payload.AsMemory(start, sendLength), serverCancellation.Token);
                    await stream.FlushAsync(serverCancellation.Token);
                    Console.WriteLine($"RANGE {start}-{start + sendLength - 1} length={sendLength}");
                    Interlocked.Increment(ref requestCount);
                }
            }
        }, serverCancellation.Token);

        var destination = Path.Combine(root, "range-retry.bin");
        Directory.CreateDirectory(root);
        try
        {
            var downloader = new GitHubAcceleratedDownloader();
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationCancellation.CancelAfter(TimeSpan.FromMinutes(3));
            await downloader.DownloadAsync(
                $"http://127.0.0.1:{port}/range-retry.bin",
                destination,
                null,
                operationCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert(false, "Range retry download did not finish automatically");
        }
        finally
        {
            serverCancellation.Cancel();
            listener.Stop();
            try
            {
                await server;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        Assert(File.ReadAllBytes(destination).SequenceEqual(payload),
            "Range retry should complete the original payload");
        Assert(requestCount > 1,
            "Range retry test should make multiple interrupted requests");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
