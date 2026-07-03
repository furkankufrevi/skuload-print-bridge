using System.Reflection;

namespace Skuload.PrintBridge;

/// <summary>Shared identity + links for the app (repo, releases, version).</summary>
internal static class AppInfo
{
    public const string DisplayName = "skuload print bridge";

    /// <summary>GitHub repo backing the Velopack update feed + releases.</summary>
    public const string RepoUrl = "https://github.com/furkankufrevi/skuload-print-bridge";

    public const string ReleasesUrl = RepoUrl + "/releases/latest";

    /// <summary>Marketing/semver version, e.g. "1.0.0".</summary>
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
