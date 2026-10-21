using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MajdataSync;

internal sealed class GameManager
{
    public const string CnbSkinList =
        "https://cnb.cool/TeamMajdata/MajdataPlay-Skins/-/git/raw/main/skins.txt";
    public const string GithubSkinList =
        "https://github.com/TeamMajdata/MajdataPlay-Skins/raw/refs/heads/main/skins.txt";
    public const string CnbSkinBase =
        "https://cnb.cool/TeamMajdata/MajdataPlay-Skins/-/git/raw/main/";
    public const string GithubSkinBase =
        "https://github.com/TeamMajdata/MajdataPlay-Skins/raw/refs/heads/main/";
    public const string StableArchive =
        "https://github.com/TeamMajdata/MajdataPlay_Build/archive/refs/tags/LATEST.zip";
    public const string StableHashes =
        "https://github.com/TeamMajdata/MajdataPlay_Build/raw/refs/heads/main/smallest_hashes.json";
    public const string TestNightlyApi =
        "https://api.github.com/repos/TeamMajdata/MajdataPlay/releases/tags/nightly";
    public const string TestNightlyAsset =
        "https://github.com/TeamMajdata/MajdataPlay/releases/download/nightly/MajdataPlay-StandaloneWindows64-nightly.zip";
    public const string TestNightlyHashes =
        "https://github.com/TeamMajdata/MajdataPlay/releases/download/nightly/smallest_hashes.json";

    private readonly ChartApi _api;
    private readonly GitHubAcceleratedDownloader _accelerator;

    public GameManager(ChartApi api)
    {
        _api = api;
        _accelerator = new GitHubAcceleratedDownloader();
    }

    public static bool IsInstalled(string gameRoot) =>
        File.Exists(Path.Combine(gameRoot, "MajdataPlay.exe"));

    public static bool IsPrerelease(string gameRoot)
    {
        var marker = Path.Combine(gameRoot, ".majdata-channel");
        return File.Exists(marker) &&
               File.ReadAllText(marker).Trim().Equals("prerelease", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasNightlyUpdate(string gameRoot)
    {
        var marker = Path.Combine(gameRoot, ".majdata-nightly-id");
        return IsPrerelease(gameRoot) && !File.Exists(marker);
    }

    public static void SetChannelMarker(string gameRoot, bool prerelease)
    {
        File.WriteAllText(
            Path.Combine(gameRoot, ".majdata-channel"),
            prerelease ? "prerelease" : "stable");
    }

    public async Task InstallGameAsync(
        string targetRoot,
        bool prerelease,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        if (!prerelease)
        {
            await InstallStableFromFilesAsync(
                targetRoot,
                preserveUserData: false,
                progress,
                cancellationToken);
            return;
        }

        await InstallGamePackageAsync(
            targetRoot,
            prerelease,
            preserveUserData: false,
            progress,
            cancellationToken);
    }

    public async Task SwitchChannelAsync(
        string targetRoot,
        bool prerelease,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        if (!prerelease)
        {
            await InstallStableFromFilesAsync(
                targetRoot,
                preserveUserData: true,
                progress,
                cancellationToken);
            return;
        }

        await InstallGamePackageAsync(
            targetRoot,
            prerelease,
            preserveUserData: true,
            progress,
            cancellationToken);
    }

    private async Task InstallStableFromFilesAsync(
        string targetRoot,
        bool preserveUserData,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(targetRoot))?.FullName
                     ?? throw new InvalidOperationException("游戏目录无效");
        Directory.CreateDirectory(parent);
        var manifestPath = Path.Combine(
            Path.GetTempPath(),
            "MajdataSync",
            "stable-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        var stagedRoot = Path.Combine(
            parent,
            ".majdata-stable-stage");
        try
        {
            progress.Report(("正在读取稳定版文件清单…", null));
            await DownloadSmallFileWithMirrorsAsync(
                StableHashes,
                manifestPath,
                cancellationToken);
            var entries = JsonSerializer.Deserialize<List<GameHashEntry>>(
                              await File.ReadAllTextAsync(manifestPath, cancellationToken),
                              JsonOptions.Default)
                          ?? [];
            if (entries.Count == 0)
            {
                throw new InvalidDataException("稳定版文件清单为空");
            }

            Directory.CreateDirectory(stagedRoot);
            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[index];
                RemoveInvalidStagedFile(stagedRoot, entry);
                if (!string.IsNullOrWhiteSpace(entry.Checksum))
                {
                    var existingPath = Path.Combine(
                        stagedRoot,
                        entry.FilePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(existingPath) &&
                        await MatchesChecksumAsync(existingPath, entry.Checksum, cancellationToken))
                    {
                        continue;
                    }
                }
                var destination = Path.Combine(
                    stagedRoot,
                    entry.FilePath.Replace('/', Path.DirectorySeparatorChar));
                progress.Report((
                    $"正在下载稳定版 {index + 1:N0}/{entries.Count:N0} · {entry.FilePath}",
                    (double)index / entries.Count));
                await DownloadAcceleratedFileAsync(
                    entry,
                    destination,
                    cancellationToken);
            }

            if (!IsInstalled(stagedRoot))
            {
                throw new InvalidDataException("稳定版文件清单中没有 MajdataPlay.exe");
            }
            await VerifyFileManifestAsync(
                stagedRoot,
                entries,
                progress,
                cancellationToken);
            SetChannelMarker(stagedRoot, prerelease: false);
            TryDelete(Path.Combine(stagedRoot, ".majdata-nightly-id"));
            if (preserveUserData && Directory.Exists(targetRoot))
            {
                CopyPreservedUserData(targetRoot, stagedRoot);
            }
            CommitStagedGame(stagedRoot, targetRoot, preserveUserData);
            TryDeleteFolder(stagedRoot);
            progress.Report(("稳定版安装完成", 1));
        }
        finally
        {
            TryDelete(manifestPath);
            TryDelete(manifestPath + ".part");
        }
    }

    private async Task InstallGamePackageAsync(
        string targetRoot,
        bool prerelease,
        bool preserveUserData,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(targetRoot))?.FullName
                     ?? throw new InvalidOperationException("游戏目录无效");
        Directory.CreateDirectory(parent);
        var zipPath = Path.Combine(parent, ".majdata-game-download.zip");
        var extractRoot = Path.Combine(parent, ".majdata-game-extract-" + Guid.NewGuid().ToString("N"));
        var stagedRoot = Path.Combine(parent, ".majdata-game-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var archiveUrl = prerelease ? TestNightlyAsset : StableArchive;
            var sources = new[]
            {
                (Name: prerelease ? "GitHub 测试版" : "GitHub 稳定版", Url: archiveUrl)
            };
            Exception? lastError = null;
            for (var attempt = 0; attempt < sources.Length; attempt++)
            {
                var sourceName = sources[attempt].Name;
                progress.Report(($"正在连接 {sourceName} 下载源…", null));
                try
                {
                    await _accelerator.DownloadAsync(
                        sources[attempt].Url,
                        zipPath,
                        new Progress<(string Source, long Bytes, long? Total, double Speed)>(value =>
                        {
                            var ratio = value.Total is > 0
                                ? Math.Clamp((double)value.Bytes / value.Total.Value, 0, 1)
                                : (double?)null;
                            var speed = value.Speed < 0
                                ? " · 网络波动，自动续传中"
                                : value.Speed > 0
                                    ? $" · {FormatBytes((long)value.Speed)}/s"
                                    : "";
                            progress.Report((
                                $"下载线路 {value.Source} · {FormatBytes(value.Bytes)} · {speed.TrimStart(' ', '·')}",
                                ratio));
                        }),
                        cancellationToken);
                    if (new FileInfo(zipPath).Length < 1_000_000)
                    {
                        throw new InvalidDataException($"{sourceName} 没有返回完整游戏包");
                    }

                    lastError = null;
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    AppLogger.Write("测试版完整包下载失败，断点保留在目标目录", ex);
                    if (attempt + 1 < sources.Length)
                    {
                        progress.Report(($"{(attempt == 0 ? "首选" : "备用")}下载源失败，正在切换 {sources[attempt + 1].Name}…", null));
                    }
                }
            }

            progress.Report(("正在校验游戏包", null));
            if (lastError is not null || !File.Exists(zipPath))
            {
                throw new IOException("两个游戏下载源都没有成功，请稍后重试", lastError);
            }

            var package = new FileInfo(zipPath);
            if (package.Length < 1_000_000)
            {
                throw new InvalidDataException("下载源没有返回完整游戏包，请换一个下载源后重试");
            }

        long? expectedLength = null;
        var lengthMarker = zipPath + ".size";
        if (File.Exists(lengthMarker) &&
            long.TryParse(
                await File.ReadAllTextAsync(lengthMarker, cancellationToken),
                out var recordedLength))
        {
            expectedLength = recordedLength;
        }
        if (expectedLength is not null && package.Length != expectedLength.Value)
        {
            throw new InvalidDataException(
                $"游戏包不完整：{package.Length:N0} / {expectedLength.Value:N0} 字节。断点已保留，重新执行会继续下载。");
        }

            await using (var stream = File.OpenRead(zipPath))
            {
                _ = await SHA256.HashDataAsync(stream, cancellationToken);
            }

            progress.Report(("正在解压游戏文件", null));
            Directory.CreateDirectory(extractRoot);
            await ExtractZipWithProgressAsync(
                zipPath,
                extractRoot,
                count => progress.Report(($"正在解压游戏文件 · {count:N0} 个", null)),
                cancellationToken);

            var sourceRoot = FindGameRoot(extractRoot);
            Directory.CreateDirectory(stagedRoot);
            CopyDirectory(sourceRoot, stagedRoot);
            if (!IsInstalled(stagedRoot))
            {
                throw new InvalidDataException("游戏包解压后没有找到 MajdataPlay.exe");
            }

            SetChannelMarker(stagedRoot, prerelease);
            if (prerelease)
            {
                var nightlyId = await ReadNightlyReleaseIdAsync(cancellationToken);
                File.WriteAllText(Path.Combine(stagedRoot, ".majdata-nightly-id"), nightlyId);
            }
            else
            {
                TryDelete(Path.Combine(stagedRoot, ".majdata-nightly-id"));
            }

            if (preserveUserData && Directory.Exists(targetRoot))
            {
                CopyPreservedUserData(targetRoot, stagedRoot);
            }

            CommitStagedGame(stagedRoot, targetRoot, preserveUserData);
            progress.Report(("游戏安装完成", 1));
        }
        finally
        {
            TryDelete(GitHubAcceleratedDownloader.RouteMarkerPath(zipPath));
            TryDelete(zipPath + ".size");
            TryDeleteFolder(extractRoot);
            TryDeleteFolder(stagedRoot);
        }
    }

    private static void CopyPreservedUserData(string sourceRoot, string targetRoot)
    {
        foreach (var name in UserDataDirectories)
        {
            var source = Path.Combine(sourceRoot, name);
            if (Directory.Exists(source))
            {
                CopyDirectory(
                    source,
                    Path.Combine(targetRoot, name),
                    skipExisting: true);
            }
        }

        var settingsSource = Path.Combine(sourceRoot, "settings.json");
        if (File.Exists(settingsSource))
        {
            File.Copy(settingsSource, Path.Combine(targetRoot, "settings.json"), overwrite: true);
        }
    }

    private static void CommitStagedGame(string stagedRoot, string targetRoot, bool preserveUserData)
    {
        Directory.CreateDirectory(targetRoot);
        if (preserveUserData)
        {
            foreach (var file in Directory.GetFiles(targetRoot))
            {
                var name = Path.GetFileName(file);
                if (!name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith(".majdata-channel", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(".majdata-nightly-id", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                }
            }

            foreach (var directory in Directory.GetDirectories(targetRoot))
            {
                if (!UserDataDirectories.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        foreach (var file in Directory.GetFiles(stagedRoot))
        {
            var name = Path.GetFileName(file);
            if (preserveUserData &&
                (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith(".majdata-channel", StringComparison.OrdinalIgnoreCase) ||
                 name.Equals(".majdata-nightly-id", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            File.Copy(file, Path.Combine(targetRoot, name), overwrite: true);
        }

        if (preserveUserData)
        {
            foreach (var name in new[] { ".majdata-channel", ".majdata-nightly-id" })
            {
                var source = Path.Combine(stagedRoot, name);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(targetRoot, name), overwrite: true);
                }
                else
                {
                    TryDelete(Path.Combine(targetRoot, name));
                }
            }
        }

        foreach (var directory in Directory.GetDirectories(stagedRoot))
        {
            var name = Path.GetFileName(directory);
            var target = Path.Combine(targetRoot, name);
            if (Directory.Exists(target))
            {
                if (preserveUserData &&
                    UserDataDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    CopyDirectory(directory, target, skipExisting: true);
                    continue;
                }

                Directory.Delete(target, recursive: true);
            }
            Directory.Move(directory, target);
        }
    }

    public static void PreserveUserDataForTest(
        string sourceRoot,
        string stagedRoot,
        string targetRoot)
    {
        CopyPreservedUserData(sourceRoot, stagedRoot);
        CommitStagedGame(stagedRoot, targetRoot, preserveUserData: true);
    }

    public static void RemoveInvalidStagedFileForTest(
        string stagedRoot,
        GameHashEntry entry)
    {
        RemoveInvalidStagedFile(stagedRoot, entry);
    }

    private static void RemoveInvalidStagedFile(string stagedRoot, GameHashEntry entry)
    {
        if (!entry.FilePath.Equals("MajdataPlay.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var existingExe = Path.Combine(
            stagedRoot,
            entry.FilePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(existingExe) && new FileInfo(existingExe).Length < 1024)
        {
            File.Delete(existingExe);
        }
    }

    public static async Task InstallStableFilesForTestAsync(
        string targetRoot,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        using var api = new ChartApi();
        var manager = new GameManager(api);
        await manager.InstallGameAsync(
            targetRoot,
            prerelease: false,
            progress,
            cancellationToken);
    }

    private static readonly string[] UserDataDirectories =
    [
        "MaiCharts",
        "Skins",
        "Cache",
        "RecordOutputs",
        "Logs"
    ];

    private static async Task DownloadAcceleratedFileAsync(
        GameHashEntry file,
        string destination,
        CancellationToken cancellationToken)
    {
        var encodedPath = string.Join('/', file.FilePath.Split('/').Select(Uri.EscapeDataString));
        var url = "https://raw.githubusercontent.com/TeamMajdata/MajdataPlay_Build/main/" + encodedPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partPath = destination + ".part";
        var candidates = (await GitHubAcceleratedDownloader.RankCandidatesAsync(
                url,
                cancellationToken))
            .Select(candidate => candidate.Url)
            .Concat(GitHubAcceleratedDownloader.CandidateUrls(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates.Count > 0
                ? candidates[attempt % candidates.Count]
                : url;
            var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            try
            {
                using var http = new HttpClient(new SocketsHttpHandler
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(12),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                })
                {
                    Timeout = TimeSpan.FromSeconds(25)
                };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.8");
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                if (existing > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
                }
                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (existing > 0 &&
                    response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    var expectedLength = response.Content.Headers.ContentRange?.Length;
                    if (expectedLength is null || existing >= expectedLength)
                    {
                        File.Move(partPath, destination, overwrite: true);
                        return;
                    }
                    File.Delete(partPath);
                    existing = 0;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var append = existing > 0 &&
                             response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (existing > 0 && !append)
                {
                    existing = 0;
                }
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    partPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                await output.DisposeAsync();
                File.Move(partPath, destination, overwrite: true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                AppLogger.Write($"稳定版文件下载失败 {file.FilePath} 第 {attempt + 1} 次", exception);
                await Task.Delay(150, cancellationToken);
            }
        }
        throw new IOException("文件下载失败：" + file.FilePath);
    }

    private static async Task<bool> MatchesChecksumAsync(
        string path,
        string checksum,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            return hash.Equals(checksum, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task VerifyFileManifestAsync(
        string root,
        IReadOnlyList<GameHashEntry> entries,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var path = Path.Combine(
                root,
                entry.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new InvalidDataException("稳定版文件缺失：" + entry.FilePath);
            }
            if (!string.IsNullOrWhiteSpace(entry.Checksum) &&
                !await MatchesChecksumAsync(path, entry.Checksum, cancellationToken))
            {
                throw new InvalidDataException("稳定版文件校验失败：" + entry.FilePath);
            }
            if (index % 50 == 0)
            {
                progress.Report((
                    $"正在校验稳定版文件 {index + 1:N0}/{entries.Count:N0}",
                    (double)index / entries.Count));
            }
        }
    }

    private static async Task ExtractZipWithProgressAsync(
        string zipPath,
        string destination,
        Action<long> report,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            long count = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(
                        Path.GetFullPath(destination) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("游戏包包含不安全路径");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                count++;
                if (count % 200 == 0)
                {
                    report(count);
                }
            }

            report(count);
        }, cancellationToken);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public async Task<GameUpdateResult> CheckGameUpdateAsync(
        string gameRoot,
        bool useCnb,
        CancellationToken cancellationToken)
    {
        var prerelease = IsPrerelease(gameRoot);
        if (prerelease)
        {
            return await CheckNightlyUpdateAsync(gameRoot, cancellationToken);
        }

        return await CheckStableUpdateAsync(gameRoot, useCnb, cancellationToken);
    }

    private async Task<GameUpdateResult> CheckNightlyUpdateAsync(
        string gameRoot,
        CancellationToken cancellationToken)
    {
        var id = await ReadNightlyReleaseIdAsync(cancellationToken);
        var marker = Path.Combine(gameRoot, ".majdata-nightly-id");
        var current = File.Exists(marker) ? await File.ReadAllTextAsync(marker, cancellationToken) : "";
        var changed = !string.Equals(current.Trim(), id, StringComparison.Ordinal);
        return new GameUpdateResult
        {
            Files = changed
                ? [new GameHashEntry { Name = "MajdataPlay-StandaloneWindows64-nightly.zip", FilePath = id, Checksum = id }]
                : [],
            ManifestUrl = TestNightlyApi
        };
    }

    private static async Task<string> ReadNightlyReleaseIdAsync(
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(
            Path.GetTempPath(),
            "MajdataSync",
            "nightly-" + Guid.NewGuid().ToString("N") + ".json");
        await DownloadSmallFileAsync(TestNightlyApi, metadataPath, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(metadataPath, cancellationToken));
            var id = document.RootElement.TryGetProperty("id", out var value)
                ? value.ToString()
                : "";
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("无法读取测试版版本号");
            }
            return id;
        }
        finally
        {
            TryDelete(metadataPath);
        }
    }

    private async Task<GameUpdateResult> CheckStableUpdateAsync(
        string gameRoot,
        bool useCnb,
        CancellationToken cancellationToken)
    {
        var manifestUrl = StableHashes;
        var manifestPath = Path.Combine(
            Path.GetTempPath(),
            "MajdataSync",
            "smallest_hashes-" + Guid.NewGuid().ToString("N") + ".json");
        await DownloadSmallFileWithMirrorsAsync(manifestUrl, manifestPath, cancellationToken);
        List<GameHashEntry> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<GameHashEntry>>(
                          await File.ReadAllTextAsync(manifestPath, cancellationToken),
                          JsonOptions.Default)
                      ?? [];
        }
        finally
        {
            TryDelete(manifestPath);
            TryDelete(manifestPath + ".part");
        }
        var changed = new List<GameHashEntry>();
        foreach (var entry in entries)
        {
            var localPath = Path.Combine(gameRoot, entry.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                changed.Add(entry);
                continue;
            }

            await using var stream = File.OpenRead(localPath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(entry.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(entry);
            }
        }

        return new GameUpdateResult { Files = changed, ManifestUrl = manifestUrl };
    }

    private static async Task DownloadSmallFileAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        Exception? lastError = null;
        using var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(35)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.2");
        http.DefaultRequestHeaders.Referrer = new Uri(new Uri(url).GetLeftPart(UriPartial.Authority) + "/");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var response = await http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var target = File.Create(temp))
                {
                    await source.CopyToAsync(target, cancellationToken);
                }

                File.Move(temp, destination, overwrite: true);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDelete(temp);
                if (attempt < 2)
                {
                    await Task.Delay(350, cancellationToken);
                }
            }
        }

        throw lastError ?? new HttpRequestException("无法读取游戏版本清单");
    }

    private static async Task DownloadSmallFileWithMirrorsAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var candidate in new[]
                 {
                     "https://ghfast.top/" + url,
                     "https://gh-proxy.com/" + url,
                     "https://ghproxy.net/" + url
                 })
        {
            try
            {
                await DownloadSmallFileAsync(candidate, destination, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        throw lastError ?? new HttpRequestException("无法读取游戏版本清单");
    }

    public async Task UpdateGameAsync(
        string gameRoot,
        IReadOnlyList<GameHashEntry> files,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        var pending = files
            .Where(file => !string.IsNullOrWhiteSpace(file.FilePath))
            .ToDictionary(file => file.FilePath, StringComparer.OrdinalIgnoreCase);
        var completedThisRun = 0;
        var failedThisRun = 0;
        var cycle = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycle++;
            var entries = pending.Values.ToArray();
            for (var index = 0; index < entries.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = entries[index];
                var destination = Path.Combine(
                    gameRoot,
                    file.FilePath.Replace('/', Path.DirectorySeparatorChar));
                var baseProgress = files.Count == 0
                    ? 0d
                    : (double)(files.Count - pending.Count) / files.Count;
                progress.Report((
                    $"第 {cycle} 轮 · 更新 {file.FilePath} ({index + 1}/{entries.Length})",
                    Math.Clamp(baseProgress, 0, 0.99)));
                try
                {
                    await DownloadGameFileResumableAsync(
                        file,
                        destination,
                        cancellationToken);
                    pending.Remove(file.FilePath);
                    completedThisRun++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    failedThisRun++;
                }
            }

            if (pending.Count == 0)
            {
                break;
            }

            var delay = TimeSpan.FromSeconds(Math.Min(30, 2 * Math.Max(1, cycle)));
            progress.Report((
                $"仍有 {pending.Count} 个文件未完成，{delay.TotalSeconds:0} 秒后自动续传",
                Math.Clamp((double)(files.Count - pending.Count) / files.Count, 0, 0.99)));
            await Task.Delay(delay, cancellationToken);
        }

        progress.Report((
            $"游戏文件更新完成 · 成功 {completedThisRun} · 重试后完成 {failedThisRun}",
            1));
    }

    private async Task DownloadGameFileResumableAsync(
        GameHashEntry file,
        string destination,
        CancellationToken cancellationToken)
    {
        var hosts = new[]
        {
            "https://github.com/TeamMajdata/MajdataPlay_Build/raw/refs/heads/main/"
        };
        var encodedPath = string.Join('/', file.FilePath.Split('/').Select(Uri.EscapeDataString));
        var url = hosts[0] + encodedPath;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partPath = destination + ".part";
        Exception? lastError = null;
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.5");
        var acceleratedUrls = (await GitHubAcceleratedDownloader.RankCandidatesAsync(
                url,
                cancellationToken))
            .Select(candidate => candidate.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateUrl = acceleratedUrls.Count > 0
                ? acceleratedUrls[attempt % acceleratedUrls.Count]
                : url;
            var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, candidateUrl);
                if (existing > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
                }

                using var response = await http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (existing > 0 && response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    var expectedLength = response.Content.Headers.ContentRange?.Length;
                    if (expectedLength is null || existing >= expectedLength)
                    {
                        File.Move(partPath, destination, overwrite: true);
                        return;
                    }
                    TryDelete(partPath);
                    existing = 0;
                    throw new InvalidDataException("断点文件长度异常，已重新开始");
                }

                response.EnsureSuccessStatusCode();
                var append = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (existing > 0 && !append)
                {
                    existing = 0;
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    partPath,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                var expected = response.Content.Headers.ContentRange?.Length ??
                               (response.Content.Headers.ContentLength is { } length
                                   ? existing + length
                                   : null);
                if (expected is not null && new FileInfo(partPath).Length != expected)
                {
                    throw new IOException("文件下载不完整，保留断点继续续传");
                }
                File.Move(partPath, destination, overwrite: true);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, attempt + 1)), cancellationToken);
            }
        }

        throw new IOException($"文件 {file.FilePath} 多次续传仍失败", lastError);
    }

    public static void LaunchGame(string gameRoot, string option)
    {
        var exe = Path.Combine(gameRoot, "MajdataPlay.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("找不到 MajdataPlay.exe", exe);
        }

        var arguments = option switch
        {
            "d3d11" => "-force-d3d11",
            "d3d12" => "-force-d3d12",
            "opengl" => "-force-glcore",
            "vulkan" => "-force-vulkan",
            "test" => "--test-mode",
            "edit" => "--view-mode",
            _ => ""
        };
        Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = gameRoot,
            UseShellExecute = true,
            Arguments = arguments
        });
    }

    public static async Task<List<SkinItem>> GetSkinsAsync(
        string gameRoot,
        bool useCnb,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.3");
        var sources = useCnb
            ? new[] { CnbSkinList, GithubSkinList }
            : new[] { GithubSkinList, CnbSkinList };
        string? text = null;
        Exception? lastError = null;
        foreach (var listUrl in sources)
        {
            try
            {
                text = await http.GetStringAsync(listUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw lastError ?? new HttpRequestException("无法读取皮肤列表");
        }

        var installed = new HashSet<string>(
            GetInstalledSkins(gameRoot).Select(item => item.Name),
            StringComparer.OrdinalIgnoreCase);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split("|-|", 2);
                var name = parts[0].Trim();
                _ = long.TryParse(parts.Length > 1 ? parts[1].Trim() : "0", out var parsedSize);
                var folderName = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? name[..^4]
                    : name;
                return new SkinItem
                {
                    Name = folderName,
                    FileName = name,
                    Size = parsedSize,
                    Installed = installed.Contains(folderName)
                };
            })
            .Where(item => item.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static List<SkinItem> GetInstalledSkins(string gameRoot)
    {
        var skinsRoot = Path.Combine(gameRoot, "Skins");
        if (!Directory.Exists(skinsRoot))
        {
            return [];
        }

        return Directory.GetDirectories(skinsRoot)
            .Where(path => !Path.GetFileName(path).StartsWith('.'))
            .Select(path => CreateInstalledSkin(gameRoot, path))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static JsonElement LoadGameSettings(string gameRoot)
    {
        var path = Path.Combine(gameRoot, "settings.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("游戏尚未生成 settings.json，请先启动一次游戏", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    public static void SaveGameSettings(string gameRoot, JsonElement settings)
    {
        var path = Path.Combine(gameRoot, "settings.json");
        var temp = path + ".majdata-tmp";
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("游戏设置必须是一个 JSON 对象");
        }

        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static string ApplySkin(string gameRoot, string skinName)
    {
        if (string.IsNullOrWhiteSpace(skinName) ||
            skinName is "." or ".." ||
            Path.GetFileName(skinName) != skinName)
        {
            throw new InvalidDataException("皮肤名称无效");
        }

        var skinPath = Path.Combine(gameRoot, "Skins", skinName);
        if (!Directory.Exists(skinPath))
        {
            throw new DirectoryNotFoundException("皮肤尚未安装：" + skinName);
        }

        var settingsPath = Path.Combine(gameRoot, "settings.json");
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException("游戏尚未生成 settings.json，请先启动一次游戏", settingsPath);
        }

        var root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                   ?? throw new InvalidDataException("settings.json 根节点不是 JSON 对象");
        if (root["Display"] is not JsonObject display)
        {
            display = new JsonObject();
            root["Display"] = display;
        }

        display["Skin"] = skinName;
        var temp = settingsPath + ".majdata-tmp";
        File.WriteAllText(
            temp,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new System.Text.UTF8Encoding(false));
        File.Move(temp, settingsPath, overwrite: true);
        return skinName;
    }

    public async Task InstallSkinAsync(
        string gameRoot,
        SkinItem skin,
        bool useCnb,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (Path.GetFileName(skin.FileName) != skin.FileName ||
            !skin.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("皮肤文件名无效");
        }

        var folderName = skin.FileName[..^4].Trim();
        if (string.IsNullOrWhiteSpace(folderName) || folderName is "." or "..")
        {
            throw new InvalidDataException("皮肤名称无效");
        }

        var baseUrl = useCnb ? CnbSkinBase : GithubSkinBase;
        var url = baseUrl + Uri.EscapeDataString(skin.FileName);
        var skinsRoot = Path.Combine(gameRoot, "Skins");
        Directory.CreateDirectory(skinsRoot);
        var zipPath = Path.Combine(skinsRoot, ".majdata-skin-" + Guid.NewGuid().ToString("N") + ".zip");
        var staging = Path.Combine(skinsRoot, ".majdata-skin-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            status?.Report("正在下载皮肤：" + skin.Name);
            await _api.DownloadFileAsync(url, zipPath, null, cancellationToken, segmentCount: 1);
            Directory.CreateDirectory(staging);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true), cancellationToken);
            var finalPath = Path.Combine(skinsRoot, folderName);
            if (Directory.Exists(finalPath))
            {
                Directory.Delete(finalPath, recursive: true);
            }

            Directory.Move(staging, finalPath);
            status?.Report("皮肤已安装：" + folderName);
        }
        finally
        {
            TryDelete(zipPath);
            TryDeleteFolder(staging);
        }
    }

    private static SkinItem CreateInstalledSkin(string gameRoot, string path)
    {
        var activeSkin = "";
        try
        {
            var settings = LoadGameSettings(gameRoot);
            if (settings.TryGetProperty("Display", out var display) &&
                display.TryGetProperty("Skin", out var skin) &&
                skin.ValueKind == JsonValueKind.String)
            {
                activeSkin = skin.GetString() ?? "";
            }
        }
        catch
        {
            // A missing settings file should not hide installed skins.
        }

        var name = Path.GetFileName(path);
        return new SkinItem
        {
            Name = name,
            FileName = name + ".zip",
            Installed = true,
            Active = string.Equals(name, activeSkin, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string FindGameRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, "MajdataPlay.exe")))
        {
            return extracted;
        }

        var candidates = Directory.GetFiles(extracted, "MajdataPlay.exe", SearchOption.AllDirectories);
        if (candidates.Length == 0)
        {
            throw new InvalidDataException("游戏包中没有 MajdataPlay.exe");
        }

        return Path.GetDirectoryName(candidates[0])!;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        bool skipExisting = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!skipExisting || !File.Exists(target))
            {
                File.Copy(file, target, overwrite: true);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)),
                skipExisting);
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
            // Best effort.
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}

internal sealed class GameHashEntry
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Checksum { get; set; } = "";
}

internal sealed class GameUpdateResult
{
    public List<GameHashEntry> Files { get; set; } = [];
    public string ManifestUrl { get; set; } = "";
}

internal sealed class SkinItem
{
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public bool Installed { get; set; }
    public bool Active { get; set; }
}
