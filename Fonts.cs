using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Skuload.PrintBridge;

/// <summary>
/// Loads the bundled Geist / Geist Mono fonts from embedded resources so the
/// app is on-brand without installing anything system-wide. Fonts are added
/// both to a <see cref="PrivateFontCollection"/> (for GDI+ drawing) and via
/// GDI's <c>AddFontMemResourceEx</c> (so WinForms' <c>TextRenderer</c>, which
/// uses GDI, can resolve them by name too). Everything falls back to Segoe UI /
/// Consolas if a face fails to load.
/// </summary>
internal static class Fonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly List<IntPtr> Pinned = new();
    private static bool _loaded;

    // Resolved family names (or system fallbacks).
    private static string _display = "Segoe UI";
    private static string _displayMedium = "Segoe UI Semibold";
    private static string _displaySemiBold = "Segoe UI Semibold";
    private static string _mono = "Consolas";

    public enum Weight { Regular, Medium, SemiBold }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

    /// <summary>Load the embedded faces. Safe to call more than once.</summary>
    public static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;

        Add("Geist-Regular.ttf");
        Add("Geist-Medium.ttf");
        Add("Geist-SemiBold.ttf");
        Add("GeistMono-Regular.ttf");

        _display = Match("Geist") ?? _display;
        _displayMedium = Match("Geist Medium") ?? _display;
        _displaySemiBold = Match("Geist SemiBold") ?? _displayMedium;
        _mono = Match("Geist Mono") ?? _mono;
    }

    /// <summary>Display (Geist) font at a pixel size.</summary>
    public static Font Display(float sizePx, Weight weight = Weight.Regular)
    {
        Load();
        var family = weight switch
        {
            Weight.SemiBold => _displaySemiBold,
            Weight.Medium => _displayMedium,
            _ => _display,
        };
        return Make(family, sizePx, FontStyle.Regular);
    }

    /// <summary>Monospace (Geist Mono) font at a pixel size.</summary>
    public static Font Mono(float sizePx, FontStyle style = FontStyle.Regular)
    {
        Load();
        return Make(_mono, sizePx, style);
    }

    private static Font Make(string family, float sizePx, FontStyle style)
    {
        try
        {
            return new Font(family, sizePx, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font("Segoe UI", sizePx, style, GraphicsUnit.Pixel);
        }
    }

    private static void Add(string logicalName)
    {
        try
        {
            var bytes = ReadResource(logicalName);
            if (bytes is null)
            {
                Logger.Warn($"Font resource not found: {logicalName}");
                return;
            }

            var ptr = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Pinned.Add(ptr); // held for the process lifetime; never freed.

            Collection.AddMemoryFont(ptr, bytes.Length);
            uint installed = 0;
            AddFontMemResourceEx(ptr, (uint)bytes.Length, IntPtr.Zero, ref installed);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load font {logicalName}", ex);
        }
    }

    /// <summary>Find a loaded family whose name matches (case-insensitive, exact-then-contains).</summary>
    private static string? Match(string wanted)
    {
        var families = Collection.Families;
        foreach (var f in families)
        {
            if (string.Equals(f.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return f.Name;
            }
        }
        foreach (var f in families)
        {
            if (f.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return f.Name;
            }
        }
        return null;
    }

    private static byte[]? ReadResource(string logicalName)
    {
        var asm = Assembly.GetExecutingAssembly();
        // Embedded with an explicit LogicalName in the csproj.
        using var stream = asm.GetManifestResourceStream(logicalName)
            ?? asm.GetManifestResourceStream(FindResourceName(asm, logicalName) ?? "");
        if (stream is null)
        {
            return null;
        }
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? FindResourceName(Assembly asm, string endsWith) =>
        asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
}
