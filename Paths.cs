namespace Skuload.PrintBridge;

/// <summary>
/// Shared on-disk locations. Everything the bridge persists lives under
/// <c>%APPDATA%\skuload\print-bridge\</c>.
/// </summary>
internal static class Paths
{
    public static string DataDir { get; } = EnsureDir(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "skuload",
            "print-bridge"));

    public static string SettingsFile { get; } = Path.Combine(DataDir, "settings.json");

    public static string LogFile { get; } = Path.Combine(DataDir, "bridge.log");

    private static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
