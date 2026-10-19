using System.Text.Json;

namespace MajdataSync;

internal static class CatalogCacheStore
{
    public static string SnapshotPath(string catalogPath) =>
        Path.ChangeExtension(catalogPath, ".snapshot.json");

    public static CatalogSnapshot Load(string catalogPath)
    {
        var path = SnapshotPath(catalogPath);
        if (!File.Exists(path))
        {
            return new CatalogSnapshot();
        }

        try
        {
            return JsonSerializer.Deserialize<CatalogSnapshot>(
                       File.ReadAllText(path),
                       JsonOptions.Default)
                   ?? new CatalogSnapshot();
        }
        catch
        {
            return new CatalogSnapshot();
        }
    }

    public static void Save(string catalogPath, IEnumerable<ChartItem> charts)
    {
        var path = SnapshotPath(catalogPath);
        var temp = path + ".tmp";
        var snapshot = new CatalogSnapshot
        {
            UpdatedAt = DateTimeOffset.Now,
            Charts = charts
                .Where(chart => !string.IsNullOrWhiteSpace(chart.Id))
                .ToDictionary(
                    chart => chart.Id,
                    chart => chart.Hash ?? "",
                    StringComparer.OrdinalIgnoreCase)
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(snapshot, JsonOptions.Compact),
            new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
