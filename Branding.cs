using System.Drawing;
using Microsoft.Win32;

namespace Skuload.PrintBridge;

/// <summary>
/// The skuload palette as <see cref="Color"/> tokens, plus light/dark
/// resolution from the current Windows theme. Values are the sRGB renderings of
/// the OKLCH tokens in <c>design.md §2</c>; Signal and Ink use the authoritative
/// brand hexes so they match the mark exactly.
/// </summary>
internal static class Branding
{
    // Signal — the only chromatic that carries meaning. Never > ~10% of a view.
    public static readonly Color Signal = FromHex("#C6F432");
    public static readonly Color SignalHover = FromHex("#B4E600");
    public static readonly Color SignalText = FromHex("#8FB600"); // signal-3, for text on light
    public static readonly Color SignalTint = FromHex("#C6F432"); // used with alpha

    // Ink — the mark's dark bars; anchors dark surfaces + light-mode text.
    public static readonly Color Ink = FromHex("#141618");

    // Semantic
    public static readonly Color Success = FromHex("#3ABA6A");
    public static readonly Color Warning = FromHex("#FAB72A");
    public static readonly Color Danger = FromHex("#F53C41");

    /// <summary>A resolved set of surface/line/text colours for one theme.</summary>
    public sealed record Palette(
        bool IsDark,
        Color Bg,
        Color BgElev,
        Color BgSunk,
        Color Line,
        Color Line2,
        Color Text,
        Color TextSoft,
        Color TextMute,
        Color TextFaint);

    public static readonly Palette Light = new(
        IsDark: false,
        Bg: FromHex("#FCFCFA"),
        BgElev: FromHex("#FFFFFF"),
        BgSunk: FromHex("#F2F2F0"),
        Line: FromHex("#E4E4E1"),
        Line2: FromHex("#D1D1CF"),
        Text: FromHex("#141618"),
        TextSoft: FromHex("#4C4D50"),
        TextMute: FromHex("#84867F"),
        TextFaint: FromHex("#9D9F9D"));

    public static readonly Palette Dark = new(
        IsDark: true,
        Bg: FromHex("#141618"),
        BgElev: FromHex("#1E1F22"),
        BgSunk: FromHex("#0E0F11"),
        Line: FromHex("#2B2C2F"),
        Line2: FromHex("#3A3B3E"),
        Text: FromHex("#F1F2F3"),
        TextSoft: FromHex("#B7B7B5"),
        TextMute: FromHex("#84867F"),
        TextFaint: FromHex("#67696B"));

    /// <summary>Resolve the palette from the user's Windows app-theme setting.</summary>
    public static Palette Current() => IsSystemDark() ? Dark : Light;

    /// <summary>True when Windows apps are set to the dark theme.</summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1 = light, 0 = dark. Absent → assume light.
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Blend <paramref name="fg"/> over <paramref name="bg"/> at the given alpha (0–1).</summary>
    public static Color Mix(Color fg, Color bg, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(fg.R * alpha + bg.R * (1 - alpha)),
            (int)Math.Round(fg.G * alpha + bg.G * (1 - alpha)),
            (int)Math.Round(fg.B * alpha + bg.B * (1 - alpha)));
    }

    private static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
