using System.Text;

namespace MajdataSync;

internal static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MajdataSync",
        "logs");

    public static string LogPath
    {
        get
        {
            Directory.CreateDirectory(LogFolder);
            return Path.Combine(LogFolder, $"majdata-sync-{DateTime.Now:yyyyMMdd}.log");
        }
    }

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [").Append(Environment.CurrentManagedThreadId).Append("] ")
                .AppendLine(message);
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never interrupt a transfer.
        }
    }

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{LogFolder}\"")
        {
            UseShellExecute = true
        });
    }
}
