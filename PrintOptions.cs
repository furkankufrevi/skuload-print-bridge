namespace Skuload.PrintBridge;

internal enum PrintFit
{
    /// <summary>Scale the label to the printable area, preserving aspect ratio.</summary>
    Fit,

    /// <summary>Print at the label's native physical size (no scaling).</summary>
    Actual,
}

/// <summary>
/// Everything a single print job needs. Built from <see cref="Settings"/>, with
/// optional per-request overrides for the printer and copy count.
/// </summary>
internal sealed class PrintOptions
{
    public string? PrinterName { get; init; }
    public int Copies { get; init; } = 1;
    public int RenderDpi { get; init; } = 300;
    public string? PaperSizeName { get; init; }
    public double MarginLeftMm { get; init; }
    public double MarginTopMm { get; init; }
    public double MarginRightMm { get; init; }
    public double MarginBottomMm { get; init; }
    public PrintFit Fit { get; init; } = PrintFit.Fit;

    public static PrintOptions FromSettings(
        Settings s,
        string? printerOverride = null,
        int? copiesOverride = null)
    {
        return new PrintOptions
        {
            PrinterName = !string.IsNullOrWhiteSpace(printerOverride)
                ? printerOverride
                : (string.IsNullOrWhiteSpace(s.PrinterName) ? null : s.PrinterName),
            Copies = copiesOverride is > 0 ? copiesOverride.Value : s.Copies,
            RenderDpi = s.RenderDpi,
            PaperSizeName = string.IsNullOrWhiteSpace(s.PaperSizeName) ? null : s.PaperSizeName,
            MarginLeftMm = s.MarginLeftMm,
            MarginTopMm = s.MarginTopMm,
            MarginRightMm = s.MarginRightMm,
            MarginBottomMm = s.MarginBottomMm,
            Fit = string.Equals(s.Scaling, "actual", StringComparison.OrdinalIgnoreCase)
                ? PrintFit.Actual
                : PrintFit.Fit,
        };
    }
}
