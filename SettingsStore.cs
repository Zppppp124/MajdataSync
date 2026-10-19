using System;
using System.IO;
using System.Text.Json;

namespace MajdataSync;

internal static class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MajdataSync");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(
                           File.ReadAllText(SettingsPath), JsonOptions.Default) ?? new AppSettings();
            }
        }
        catch
        {
            // A corrupt preference file should not prevent startup.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions.Default));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
