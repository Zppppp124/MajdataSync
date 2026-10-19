using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MajdataSync;

internal sealed class GameManager
{
    public const string CnbArchive =
        "https://cnb.cool/TeamMajdata/MajdataPlay_Build/-/git/archive/refs/heads/main.zip";
    public const string GithubArchive =
        "https://github.com/TeamMajdata/MajdataPlay_Build/archive/refs/tags/LATEST.zip";
    public const string CnbHashes =
        "https://cnb.cool/TeamMajdata/MajdataPlay_Build/-/git/raw/main/smallest_hashes.json?download=true";
    public const string GithubHashes =
        "https://github.com/TeamMajdata/MajdataPlay_Build/raw/refs/heads/main/smallest_hashes.json";
    public const string CnbSkinList =
        "https://cnb.cool/TeamMajdata/MajdataPlay-Skins/-/git/raw/main/skins.txt";
    public const string GithubSkinList =
        "https://github.com/TeamMajdata/MajdataPlay-Skins/raw/refs/heads/main/skins.txt";
    public const string CnbSkinBase =
        "https://cnb.cool/TeamMajdata/MajdataPlay-Skins/-/git/raw/main/";
    public const string GithubSkinBase =
        "https://github.com/TeamMajdata/MajdataPlay-Skins/raw/refs/heads/main/";

    private readonly ChartApi _api;

    public GameManager(ChartApi api)
    {
        _api = api;
    }

    public static bool IsInstalled(string gameRoot) =>
        File.Exists(Path.Combine(gameRoot, "MajdataPlay.exe"));

    public async Task InstallGameAsync(
        string targetRoot,
        bool useCnb,
        IProgress<(string Message, double? Progress)> progress,
        CancellationToken cancellationToken)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(targetRoot))?.FullName
                     ?? throw new InvalidOperationException("游戏目录无效");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(targetRoot);
        var zipPath = Path.Combine(parent, ".majdata-game-download.zip");
        var extractRoot = Path.Combine(parent, ".majdata-game-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sources = useCnb
                ? new[] { CnbArchive, GithubArchive }
                : new[] { GithubArchive, CnbArchive };
            Exception? lastError = null;
            for (var attempt = 0; attempt < sources.Length; attempt++)
            {
                var sourceName = sources[attempt].Contains("github.com", StringComparison.OrdinalIgnoreCase)
                    ? "GitHub"
                    : "CNB";
                progress.Report(($"正在连接 {sourceName} 下载源…", null));
                var downloadProgress = new Progress<DownloadProgress>(value =>
                {
                    var ratio = value.TotalBytes is > 0
                        ? Math.Clamp((double)value.BytesReceived / value.TotalBytes.Value, 0, 1)
                        : (double?)null;
                    var speed = value.SpeedBytesPerSecond > 0
                        ? $" · {FormatBytes((long)value.SpeedBytesPerSecond)}/s"
                        : "";
                    var size = value.TotalBytes is > 0
                        ? $"{FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes.Value)}"
                        : FormatBytes(value.BytesReceived);
                    progress.Report(($"正在从 {sourceName} 下载游戏包 · {size}{speed}", ratio));
                });
                try
                {
                    await _api.DownloadFileAsync(
                        sources[attempt],
                        zipPath,
                        downloadProgress,
                        cancellationToken,
                        segmentCount: 4,
                        stallTimeout: TimeSpan.FromSeconds(25));
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
                    TryDelete(zipPath);
                    TryDelete(zipPath + ".part");
                    if (attempt + 1 < sources.Length)
                    {
                        progress.Report(($"{(attempt == 0 ? "首选" : "备用")}下载源失败，正在切换 {(sources[attempt + 1].Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "GitHub" : "CNB")}…", null));
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
            CopyDirectory(sourceRoot, targetRoot);
            if (!IsInstalled(targetRoot))
            {
                throw new InvalidDataException("游戏包解压后没有找到 MajdataPlay.exe");
            }

            progress.Report(("游戏安装完成", 1));
        }
        finally
        {
            TryDelete(zipPath);
            TryDeleteFolder(extractRoot);
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
        var manifestUrl = useCnb ? CnbHashes : GithubHashes;
        var manifestPath = Path.Combine(
            Path.GetTempPath(),
            "MajdataSync",
            "smallest_hashes-" + Guid.NewGuid().ToString("N") + ".json");
        await DownloadSmallFileAsync(manifestUrl, manifestPath, cancellationToken);
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
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.2");
        http.DefaultRequestHeaders.Referrer = new Uri(new Uri(url).GetLeftPart(UriPartial.Authority) + "/");
        for (var attempt = 0; attempt < 2; attempt++)
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
                if (attempt == 0)
                {
                    await Task.Delay(350, cancellationToken);
                }
            }
        }

        throw lastError ?? new HttpRequestException("无法读取游戏版本清单");
    }

    public async Task UpdateGameAsync(
        string gameRoot,
        bool useCnb,
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
                        useCnb,
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
        bool useCnb,
        CancellationToken cancellationToken)
    {
        var hosts = useCnb
            ? new[]
            {
                "https://cnb.cool/TeamMajdata/MajdataPlay_Build/-/git/raw/main/",
                "https://github.com/TeamMajdata/MajdataPlay_Build/raw/refs/heads/main/"
            }
            : new[]
            {
                "https://github.com/TeamMajdata/MajdataPlay_Build/raw/refs/heads/main/",
                "https://cnb.cool/TeamMajdata/MajdataPlay_Build/-/git/raw/main/"
            };
        var encodedPath = string.Join('/', file.FilePath.Split('/').Select(Uri.EscapeDataString));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partPath = destination + ".part";
        Exception? lastError = null;
        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MajdataSync/1.5");
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseUrl = hosts[attempt % hosts.Length];
            var url = baseUrl + encodedPath;
            var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
