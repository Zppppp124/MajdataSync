using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MajdataSync;

internal sealed class ChartItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Designer { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string?> Levels { get; set; } = [];
    public string Uploader { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public string Hash { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<string> PublicTags { get; set; } = [];

    [JsonIgnore]
    public string DateText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public string LevelText
    {
        get
        {
            var labels = new[] { "BAS", "ADV", "EXP", "MST", "RE:MASTER", "?", "?" };
            var values = Levels
                .Select((level, index) => string.IsNullOrWhiteSpace(level)
                    ? null
                    : $"{labels[Math.Min(index, labels.Length - 1)]} {level.Trim()}")
                .Where(value => value is not null)
                .ToArray();
            return values.Length == 0 ? "未标注难度" : string.Join("  ·  ", values);
        }
    }

    [JsonIgnore]
    public string ImageUrl => ChartApi.Image(Id, full: false);
}

internal sealed class AppSettings
{
    public string? LastRootPath { get; set; }
    public string? LastExplicitOriginalPath { get; set; }
    public int Concurrency { get; set; } = 6;
    public bool IncludeVideo { get; set; } = true;
    public bool AutoCheck { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public string DownloadMode { get; set; } = "auto";
    public bool UseMirrors { get; set; } = true;
    public string StorageTarget { get; set; } = "both";
    public string GameDownloadSource { get; set; } = "github";
    public string GameChannel { get; set; } = "stable";
    public string? GameRootPath { get; set; }
}

internal sealed class SyncRecord
{
    public string ChartId { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Folder { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset DownloadedAt { get; set; }
    public bool HasVideo { get; set; }
    public string ZipName { get; set; } = "";
}

internal sealed class SyncInventory
{
    public int Version { get; set; } = 1;
    public string RootPath { get; set; } = "";
    public Dictionary<string, SyncRecord> Charts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? LastSyncAt { get; set; }
}

internal sealed class FolderInfo
{
    public required string RootPath { get; init; }
    public required string GameRoot { get; init; }
    public required string ChartsRoot { get; init; }
    public bool DetectedAutomatically { get; init; }
    public int ExistingChartFolders { get; init; }
    public int SyncedChartFolders { get; init; }
}

internal sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double SpeedBytesPerSecond);

internal sealed class SyncRequest
{
    public bool UpdatedOnly { get; set; }
    public string? ChartId { get; set; }
    public List<string> ChartIds { get; set; } = [];
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? Search { get; set; }
    public int? Limit { get; set; }
    public bool IncludeVideo { get; set; } = true;
    public int Concurrency { get; set; } = 6;
    public string DownloadMode { get; set; } = "auto";
    public bool UseMirrors { get; set; } = true;
    public string StorageTarget { get; set; } = "both";
}

internal sealed class ImportZipRequest
{
    public string? ZipPath { get; set; }
}

internal sealed class AppStatus
{
    public string Version { get; set; } = "1.0.0";
    public bool OperationRunning { get; set; }
    public string Site { get; set; } = ChartApi.SiteUrl;
}

internal sealed class CatalogQuery
{
    public int Page { get; set; }
    public int PageSize { get; set; } = 24;
    public string? Search { get; set; }
    public string Status { get; set; } = "all";
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
}

internal sealed class CatalogSnapshot
{
    public Dictionary<string, string> Charts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class GameInstallRequest
{
    public string? Source { get; set; }
    public string? Channel { get; set; }
    public string? TargetPath { get; set; }
}

internal sealed class GameChannelRequest
{
    public string? Channel { get; set; }
}

internal sealed class GameLaunchRequest
{
    public string? Option { get; set; }
}

internal sealed class GameSettingUpdate
{
    public string? Section { get; set; }
    public string? Key { get; set; }
    public JsonElement Value { get; set; }
}

internal sealed class SkinInstallRequest
{
    public string? Name { get; set; }
    public string? FileName { get; set; }
    public long Size { get; set; }
}

internal sealed class SkinApplyRequest
{
    public string? Name { get; set; }
}
