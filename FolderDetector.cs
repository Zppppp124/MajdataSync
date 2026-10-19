using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MajdataSync;

internal static partial class FolderDetector
{
    public static FolderInfo? DetectFrom(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(selectedPath.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }

        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        if (name.Equals("Original", StringComparison.OrdinalIgnoreCase))
        {
            return Build(fullPath, detected: false);
        }

        var directOriginal = Path.Combine(fullPath, "Original");
        if (name.Equals("MaiCharts", StringComparison.OrdinalIgnoreCase) && Directory.Exists(directOriginal))
        {
            return Build(directOriginal, detected: true);
        }

        var childCharts = Path.Combine(fullPath, "MaiCharts");
        var childOriginal = Path.Combine(childCharts, "Original");
        if (Directory.Exists(childOriginal))
        {
            return Build(childOriginal, detected: true);
        }

        if (LooksLikeMajdataInstall(fullPath) && Directory.Exists(directOriginal))
        {
            return Build(directOriginal, detected: true);
        }

        return null;
    }

    public static FolderInfo? DetectNearby(string? hintPath)
    {
        foreach (var start in CandidateStarts(hintPath))
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            try
            {
                var directory = new DirectoryInfo(start);
                for (var depth = 0; depth < 5 && directory is not null; depth++)
                {
                    var found = DetectFrom(directory.FullName);
                    if (found is not null)
                    {
                        return found;
                    }

                    directory = directory.Parent;
                }
            }
            catch
            {
                // Keep trying other likely locations.
            }
        }

        return null;
    }

    private static IEnumerable<string?> CandidateStarts(string? hintPath)
    {
        var starts = new List<string?>
        {
            hintPath,
            TryGetPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MajdataPlay.exe"),
            TryGetPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MajdataPlay.exe"),
            TryGetPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MajdataPlay", "MajdataPlay.exe"),
            TryGetPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MajdataPlay", "MajdataPlay.exe")
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.Network)
                {
                    continue;
                }

                starts.Add(TryGetPath(drive.RootDirectory.FullName, "MajdataPlay.exe"));
                starts.Add(TryGetPath(drive.RootDirectory.FullName, "Games", "MajdataPlay.exe"));
                starts.Add(TryGetPath(drive.RootDirectory.FullName, "majtest", "MajdataPlay.exe"));
            }
            catch
            {
                // Drive enumeration is opportunistic.
            }
        }

        return starts;
    }

    private static string? TryGetPath(params string[] parts)
    {
        try
        {
            var path = Path.Combine(parts);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeMajdataInstall(string path)
    {
        return File.Exists(Path.Combine(path, "MajdataPlay.exe")) ||
               (Directory.Exists(Path.Combine(path, "MaiCharts")) &&
                File.Exists(Path.Combine(path, "settings.json")));
    }

    private static FolderInfo Build(string originalPath, bool detected)
    {
        var fullOriginal = Path.GetFullPath(originalPath);
        if (!Directory.Exists(fullOriginal))
        {
            Directory.CreateDirectory(fullOriginal);
        }

        var chartsRoot = Directory.GetParent(fullOriginal)?.FullName ?? fullOriginal;
        var gameRoot = Directory.GetParent(chartsRoot)?.FullName ?? chartsRoot;

        var folders = 0;
        var synced = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(fullOriginal))
            {
                if (Path.GetFileName(dir).StartsWith('.'))
                {
                    continue;
                }

                folders++;
                if (File.Exists(Path.Combine(dir, ".majdata-sync.json")) ||
                    File.Exists(Path.Combine(dir, "id")))
                {
                    synced++;
                }
            }
        }
        catch
        {
            // Folder count is informational only.
        }

        return new FolderInfo
        {
            RootPath = fullOriginal,
            ChartsRoot = chartsRoot,
            GameRoot = gameRoot,
            DetectedAutomatically = detected,
            ExistingChartFolders = folders,
            SyncedChartFolders = synced
        };
    }

    public static string SafeFolderName(string input)
    {
        var name = string.IsNullOrWhiteSpace(input) ? "Untitled" : input.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = WhitespaceRegex().Replace(name, " ").Trim().TrimEnd('.');
        if (name.Length > 96)
        {
            name = name[..96].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Untitled";
        }

        var reserved = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            name += "_";
        }

        return name;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
