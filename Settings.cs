using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skuload.PrintBridge;

/// <summary>
/// User-editable configuration, persisted as JSON. Defaults are chosen so the
/// bridge works out of the box: default Windows printer, a fixed localhost
/// port, and an origin allow-list covering local dev + the production app.
/// </summary>
internal sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Localhost TCP port the HTTP server binds to.</summary>
    public int Port { get; set; } = 7654;

    /// <summary>
    /// Printer name to use. Empty/null means the current Windows default printer.
    /// </summary>
    public string? PrinterName { get; set; }

    /// <summary>Copies per print job (1–10).</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Render DPI for PDF labels. 203 ≈ thermal, 300 = crisp general.</summary>
    public int RenderDpi { get; set; } = 300;

    /// <summary>
    /// Paper/page size to print on, by driver name (e.g. "4 x 6"). Null/empty
    /// means the printer's own default page size.
    /// </summary>
    public string? PaperSizeName { get; set; }

    /// <summary>Page margins in millimetres.</summary>
    public double MarginLeftMm { get; set; }

    public double MarginTopMm { get; set; }

    public double MarginRightMm { get; set; }

    public double MarginBottomMm { get; set; }

    /// <summary>
    /// How the label is sized on the page: "fit" scales it to the printable
    /// area (aspect preserved); "actual" prints at the label's native size.
    /// </summary>
    public string Scaling { get; set; } = "fit";

    /// <summary>Launch automatically when the user logs into Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Set once the first-run welcome has been shown.</summary>
    public bool WelcomeShown { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var json = File.ReadAllText(Paths.SettingsFile);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (loaded is not null)
                {
                    return loaded.Sanitized();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to read settings; using defaults", ex);
        }

        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(Sanitized(), JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to write settings", ex);
        }
    }

    private Settings Sanitized()
    {
        if (Port is < 1024 or > 65535)
        {
            Port = 7654;
        }

        Copies = Math.Clamp(Copies, 1, 10);
        RenderDpi = Math.Clamp(RenderDpi, 72, 600);
        MarginLeftMm = Math.Clamp(MarginLeftMm, 0, 100);
        MarginTopMm = Math.Clamp(MarginTopMm, 0, 100);
        MarginRightMm = Math.Clamp(MarginRightMm, 0, 100);
        MarginBottomMm = Math.Clamp(MarginBottomMm, 0, 100);
        Scaling = string.Equals(Scaling, "actual", StringComparison.OrdinalIgnoreCase) ? "actual" : "fit";
        return this;
    }
}
