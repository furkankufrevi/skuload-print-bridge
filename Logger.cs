using System.Text;

namespace Skuload.PrintBridge;

/// <summary>
/// Minimal append-only file logger with an in-memory tail for the tray's
/// "Open logs" action. Thread-safe; the HTTP server and the UI thread both
/// write to it.
/// </summary>
internal static class Logger
{
    private const long MaxBytes = 1_000_000; // ~1 MB before we roll the file.
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR ", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                RollIfTooLargeUnlocked();
                File.AppendAllText(Paths.LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // A logger that throws is worse than one that drops a line.
            }
        }
    }

    private static void RollIfTooLargeUnlocked()
    {
        try
        {
            var info = new FileInfo(Paths.LogFile);
            if (info.Exists && info.Length > MaxBytes)
            {
                var bak = Paths.LogFile + ".1";
                File.Delete(bak);
                File.Move(Paths.LogFile, bak);
            }
        }
        catch
        {
            // Ignore roll failures; we'll just keep appending.
        }
    }
}
