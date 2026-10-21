using System.Security.Cryptography;
using System.Text;

namespace MajdataSync;

internal static class OnlineCacheWriter
{
    public static string CacheRoot(FolderInfo root) =>
        Path.Combine(root.GameRoot, "Cache", "Net", "MajdataNET");

    public static string CacheFolder(FolderInfo root, string base64Hash)
    {
        var bytes = Convert.FromBase64String(base64Hash);
        return Path.Combine(CacheRoot(root), HexFolderName(bytes));
    }

    public static async Task<SyncRecord> CopyToFolderAsync(
        FolderInfo destination,
        ChartItem chart,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        var targetFolder = Path.Combine(
            destination.RootPath,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceFolder)));
        Directory.CreateDirectory(targetFolder);
        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(
                file,
                Path.Combine(targetFolder, Path.GetFileName(file)),
                overwrite: true);
        }

        await PublishAsync(destination, chart, targetFolder, cancellationToken);
        return CreateRecord(chart, destination, targetFolder);
    }

    public static string HexFolderName(byte[] bytes) =>
        string.Join("-", bytes.Select(value => value.ToString("X2")));

    public static async Task<SyncRecord> PublishAsync(
        FolderInfo root,
        ChartItem chart,
        string sourceFolder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chart.Hash))
        {
            throw new InvalidDataException("谱面缺少站点哈希，无法生成在线缓存");
        }

        var target = CacheFolder(root, chart.Hash);
        var staging = target + ".majdata-staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            Directory.CreateDirectory(staging);
            var copies = new (string Source, string Destination)[]
            {
                (Path.Combine(sourceFolder, "maidata.txt"), Path.Combine(staging, "maidata.txt")),
                (Path.Combine(sourceFolder, "track.mp3"), Path.Combine(staging, "track.mp3")),
                (Path.Combine(sourceFolder, "bg.jpg"), Path.Combine(staging, "bg.jpg")),
                (Path.Combine(sourceFolder, "pv.mp4"), Path.Combine(staging, "bg.mp4"))
            };
            foreach (var (source, destination) in copies)
            {
                if (!File.Exists(source))
                {
                    continue;
                }

                File.Copy(source, destination, overwrite: true);
            }

            var fullSizeCover = Path.Combine(staging, "bg_fullSize.jpg");
            if (!File.Exists(fullSizeCover))
            {
                File.Copy(Path.Combine(staging, "bg.jpg"), fullSizeCover);
            }

            foreach (var file in Directory.EnumerateFiles(staging).ToArray())
            {
                await WriteHashAsync(file, cancellationToken);
            }

            var oldCache = target + ".majdata-old-" + Guid.NewGuid().ToString("N");
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Move(target, oldCache);
                }

                Directory.Move(staging, target);
                if (Directory.Exists(oldCache))
                {
                    Directory.Delete(oldCache, recursive: true);
                }
            }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(oldCache))
                {
                    Directory.Move(oldCache, target);
                }
                throw;
            }

            return CreateRecord(chart, root, target);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                TryDeleteFolder(staging);
            }
        }
    }

    public static async Task<SyncRecord> PublishFromCacheAsync(
        FolderInfo root,
        ChartItem chart,
        CancellationToken cancellationToken)
    {
        var cacheFolder = CacheFolder(root, chart.Hash);
        if (!Directory.Exists(cacheFolder))
        {
            throw new InvalidDataException("未找到该曲目的在线缓存");
        }

        return await PublishAsync(root, chart, cacheFolder, cancellationToken);
    }

    private static async Task WriteHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var hashText = Convert.ToBase64String(hashBytes);
        await File.WriteAllTextAsync(path + ".sha256", hashText, new UTF8Encoding(false), cancellationToken);
    }

    private static SyncRecord CreateRecord(ChartItem chart, FolderInfo root, string cacheFolder) =>
        new()
        {
            ChartId = chart.Id,
            Hash = chart.Hash,
            Title = chart.Title,
            Artist = chart.Artist,
            Folder = Path.GetRelativePath(root.RootPath, cacheFolder),
            UploadedAt = chart.Timestamp,
            DownloadedAt = DateTimeOffset.Now,
            HasVideo = File.Exists(Path.Combine(cacheFolder, "bg.mp4")),
            ZipName = chart.Title + ".zip"
        };

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
            // A later sync can clean up an abandoned staging folder.
        }
    }
}
