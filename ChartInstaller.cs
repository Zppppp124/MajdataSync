using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataSync;

internal sealed class ChartInstaller
{
    private static readonly string[] RequiredFiles = ["maidata.txt", "track.mp3", "bg.jpg"];
    private readonly ChartApi _api;

    public ChartInstaller(ChartApi api)
    {
        _api = api;
    }

    public static string ChooseFolder(string originalRoot, ChartItem chart)
    {
        var baseName = FolderDetector.SafeFolderName(chart.Title);
        var candidate = Path.Combine(originalRoot, baseName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        var existingId = ReadId(candidate);
        if (existingId.Length > 0 && existingId.Equals(chart.Id, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        var suffix = 2;
        while (true)
        {
            var alternate = Path.Combine(originalRoot, $"{baseName} ({suffix})");
            if (!Directory.Exists(alternate) && !File.Exists(alternate))
            {
                return alternate;
            }

            existingId = ReadId(alternate);
            if (existingId.Length > 0 && existingId.Equals(chart.Id, StringComparison.OrdinalIgnoreCase))
            {
                return alternate;
            }

            suffix++;
        }
    }

    public async Task<SyncRecord?> DownloadChartAsync(
        FolderInfo root,
        ChartItem chart,
        bool includeVideo,
        int concurrency,
        Func<string, double?, Task> progress,
        CancellationToken cancellationToken)
    {
        var targetFolder = ChooseFolder(root.RootPath, chart);
        var workRoot = Path.Combine(root.RootPath, ".majdata-work");
        var tempFolder = Path.Combine(workRoot, "downloads", chart.Id);
        Directory.CreateDirectory(tempFolder);
        TrySetHidden(workRoot);
        try
        {
            var resources = new List<(string Name, string Resource)>
            {
                ("track.mp3", "track"),
                ("bg.jpg", "image"),
                ("maidata.txt", "chart")
            };
            if (includeVideo)
            {
                resources.Add(("pv.mp4", "video"));
            }

            var mode = SettingsStore.Load().DownloadMode;
            var segmentCount = mode switch
            {
                "single" => 1,
                "4" => 4,
                "2" => 2,
                _ => 2
            };

            await Parallel.ForEachAsync(
                resources,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(concurrency, 1, 4),
                    CancellationToken = cancellationToken
                },
                async (resource, token) =>
                {
                    var localProgress = new Progress<DownloadProgress>(value =>
                    {
                        var percent = value.TotalBytes is > 0
                            ? (double?)value.BytesReceived / value.TotalBytes
                            : null;
                        _ = progress(
                            $"{resource.Name}  {FormatBytes(value.BytesReceived)} / " +
                            $"{(value.TotalBytes is > 0 ? FormatBytes(value.TotalBytes.Value) : "…")}  " +
                            $"{FormatBytes((long)value.SpeedBytesPerSecond)}/s",
                            percent);
                    });

                    try
                    {
                        var destination = Path.Combine(tempFolder, resource.Name);
                        if (resource.Resource != "video" &&
                            File.Exists(destination) &&
                            new FileInfo(destination).Length > 0)
                        {
                            await progress($"{resource.Name} 已存在，跳过远程下载", 1.0);
                            return;
                        }

                        var hosts = await _api.MirrorHostsForDownloadAsync(token);
                        Exception? lastError = null;
                        foreach (var host in hosts)
                        {
                            try
                            {
                                await _api.DownloadFileAsync(
                                    ChartApi.ResourceUrl(host, chart.Id, resource.Resource),
                                    Path.Combine(tempFolder, resource.Name),
                                    localProgress,
                                    token,
                                    segmentCount);
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
                                await progress($"{resource.Name} 镜像 {host} 不可用，切换中…", null);
                            }
                        }

                        if (lastError is not null)
                        {
                            throw lastError;
                        }
                    }
                    catch (Exception) when (resource.Name == "pv.mp4")
                    {
                        TryDelete(Path.Combine(tempFolder, resource.Name));
                        await progress("视频不可用，已跳过", null);
                    }
                });

            foreach (var required in RequiredFiles)
            {
                var path = Path.Combine(tempFolder, required);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                {
                    throw new InvalidDataException($"下载不完整：缺少 {required}");
                }
            }

            NormalizeChartFolder(tempFolder);
            var hasVideo = File.Exists(Path.Combine(tempFolder, "pv.mp4"));
            await File.WriteAllTextAsync(Path.Combine(tempFolder, "id"), chart.Hash, cancellationToken);
            var record = CreateRecord(chart, targetFolder, root.RootPath, hasVideo);
            await File.WriteAllTextAsync(
                Path.Combine(tempFolder, ".majdata-sync.json"),
                JsonSerializer.Serialize(record, JsonOptions.Default),
                cancellationToken);

            CommitFolder(tempFolder, targetFolder, root.RootPath);
            return record;
        }
        catch (OperationCanceledException)
        {
            await progress("已取消或连接超时，缓存已保留", null);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            await progress("下载失败，缓存已保留：" + ex.Message, null);
            return null;
        }
        catch
        {
            TryDeleteFolder(tempFolder);
            throw;
        }
    }

    public async Task<SyncRecord> ImportZipAsync(
        FolderInfo root,
        string zipPath,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("找不到 ZIP 文件", zipPath);
        }

        var zipName = Path.GetFileNameWithoutExtension(zipPath);
        var folderName = FolderDetector.SafeFolderName(zipName);
        var targetFolder = ChooseZipFolder(root.RootPath, folderName);
        var workRoot = Path.Combine(root.RootPath, ".majdata-work");
        var tempFolder = Path.Combine(workRoot, "imports", Guid.NewGuid().ToString("N"));
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, recursive: true);
        }

        Directory.CreateDirectory(tempFolder);
        TrySetHidden(workRoot);
        try
        {
            status?.Report("正在校验压缩包…");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException("压缩包为空");
                }

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (normalized.StartsWith('/') || normalized.Contains("../", StringComparison.Ordinal) ||
                        normalized.Contains("/..", StringComparison.Ordinal) ||
                        Path.IsPathRooted(normalized))
                    {
                        throw new InvalidDataException("压缩包包含不安全的路径");
                    }
                }

                status?.Report("正在解压谱面…");
                await Task.Run(() => archive.ExtractToDirectory(tempFolder, overwriteFiles: true), cancellationToken);
            }

            NormalizeChartFolder(tempFolder);
            if (!File.Exists(Path.Combine(tempFolder, "maidata.txt")))
            {
                throw new InvalidDataException("压缩包里没有找到 maidata.txt，无法作为 Majdata 谱面导入");
            }

            CommitFolder(tempFolder, targetFolder, root.RootPath);
            status?.Report($"已导入到 {Path.GetFileName(targetFolder)}");

            return new SyncRecord
            {
                ChartId = "",
                Hash = "",
                Title = zipName,
                Folder = Path.GetFileName(targetFolder),
                DownloadedAt = DateTimeOffset.Now,
                ZipName = Path.GetFileName(zipPath),
                HasVideo = File.Exists(Path.Combine(targetFolder, "pv.mp4"))
            };
        }
        catch
        {
            TryDeleteFolder(tempFolder);
            throw;
        }
    }

    public static string? FindMaidataTitle(string folder)
    {
        var maidata = Path.Combine(folder, "maidata.txt");
        if (!File.Exists(maidata))
        {
            return null;
        }

        foreach (var line in File.ReadLines(maidata).Take(30))
        {
            if (line.StartsWith("&title=", StringComparison.OrdinalIgnoreCase))
            {
                return line["&title=".Length..].Trim();
            }
        }

        return null;
    }

    private static string ChooseZipFolder(string originalRoot, string folderName)
    {
        var candidate = Path.Combine(originalRoot, folderName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (true)
        {
            var alternate = Path.Combine(originalRoot, $"{folderName} ({suffix})");
            if (!Directory.Exists(alternate) && !File.Exists(alternate))
            {
                return alternate;
            }

            suffix++;
        }
    }

    private static void NormalizeChartFolder(string folder)
    {
        if (File.Exists(Path.Combine(folder, "maidata.txt")))
        {
            return;
        }

        var candidates = Directory.GetDirectories(folder)
            .Where(dir => File.Exists(Path.Combine(dir, "maidata.txt")))
            .ToArray();
        if (candidates.Length != 1)
        {
            return;
        }

        var inner = candidates[0];
        foreach (var file in Directory.EnumerateFiles(inner))
        {
            File.Move(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(inner))
        {
            var destination = Path.Combine(folder, Path.GetFileName(dir));
            if (Directory.Exists(destination))
            {
                CopyDirectory(dir, destination);
            }
            else
            {
                Directory.Move(dir, destination);
            }
        }

        Directory.Delete(inner, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    private static void CommitFolder(string source, string destination, string originalRoot)
    {
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException("目标曲目目录无效");
        }

        if (!IsPathInside(destination, originalRoot))
        {
            throw new InvalidOperationException("目标曲目目录不在 Original 内");
        }

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            $".{Path.GetFileName(destination)}.majdata-staging-{Guid.NewGuid():N}");
        try
        {
            // The source and staging folders both live on the Original volume.
            CopyDirectory(source, staging);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            Directory.Move(staging, destination);
            TryDeleteFolder(source);
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    private static bool IsPathInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) +
                       Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TrySetHidden(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.Hidden);
            }
        }
        catch
        {
            // Hidden is cosmetic; downloading must still work.
        }
    }

    private static SyncRecord CreateRecord(ChartItem chart, string targetFolder, string rootPath, bool hasVideo)
    {
        return new SyncRecord
        {
            ChartId = chart.Id,
            Hash = chart.Hash,
            Title = chart.Title,
            Artist = chart.Artist,
            Folder = Path.GetRelativePath(rootPath, targetFolder),
            UploadedAt = chart.Timestamp,
            DownloadedAt = DateTimeOffset.Now,
            HasVideo = hasVideo,
            ZipName = FolderDetector.SafeFolderName(chart.Title) + ".zip"
        };
    }

    private static string ReadId(string folder)
    {
        var idPath = Path.Combine(folder, "id");
        if (File.Exists(idPath))
        {
            try
            {
                return File.ReadAllText(idPath).Trim();
            }
            catch
            {
                return "";
            }
        }

        var recordPath = Path.Combine(folder, ".majdata-sync.json");
        if (!File.Exists(recordPath))
        {
            return "";
        }

        try
        {
            var record = JsonSerializer.Deserialize<SyncRecord>(
                File.ReadAllText(recordPath), JsonOptions.Default);
            return record?.ChartId ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var amount = (double)value;
        var index = 0;
        while (amount >= 1024 && index < units.Length - 1)
        {
            amount /= 1024;
            index++;
        }

        return $"{amount:0.#} {units[index]}";
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
