using System.Drawing;
using System.Drawing.Printing;
using PDFtoImage;
using SkiaSharp;

namespace Skuload.PrintBridge;

internal enum LabelFormat
{
    Unknown,
    Pdf,
    Png,
    Jpeg,
}

/// <summary>A paper/page size a printer supports, with its physical dimensions.</summary>
internal readonly record struct PaperOption(string Name, double WidthMm, double HeightMm);

/// <summary>
/// Turns a label (PDF or image) into physical paper. PDFs are rasterised with
/// pdfium then printed as bitmaps; images print directly. Printing is fully
/// silent — <see cref="StandardPrintController"/> suppresses the OS progress
/// dialog so nothing pops up on the operator's screen. Paper size, margins and
/// scaling come from <see cref="PrintOptions"/>.
/// </summary>
internal static class PrintService
{
    private const double MmPerHundredthInch = 0.254; // 1/100 inch = 0.254 mm

    public static IReadOnlyList<string> InstalledPrinters()
    {
        var list = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            list.Add(name);
        }
        return list;
    }

    public static string DefaultPrinter() => new PrinterSettings().PrinterName;

    /// <summary>Page sizes the given printer (or the default) supports.</summary>
    public static IReadOnlyList<PaperOption> PaperSizes(string? printerName)
    {
        var settings = new PrinterSettings();
        if (!string.IsNullOrWhiteSpace(printerName))
        {
            settings.PrinterName = printerName;
        }
        if (!settings.IsValid)
        {
            settings = new PrinterSettings();
        }

        var list = new List<PaperOption>();
        try
        {
            foreach (PaperSize size in settings.PaperSizes)
            {
                list.Add(new PaperOption(
                    size.PaperName,
                    Math.Round(size.Width * MmPerHundredthInch, 1),
                    Math.Round(size.Height * MmPerHundredthInch, 1)));
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Enumerating paper sizes failed", ex);
        }
        return list;
    }

    /// <summary>Sniff the byte signature so callers don't have to trust a hint.</summary>
    public static LabelFormat Detect(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46)
        {
            return LabelFormat.Pdf; // "%PDF"
        }
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return LabelFormat.Png;
        }
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return LabelFormat.Jpeg;
        }
        return LabelFormat.Unknown;
    }

    /// <summary>
    /// Print a label. Returns the printer that was actually used and the page
    /// count. Throws on an unprintable payload or a printing failure.
    /// </summary>
    public static (string Printer, int Pages) Print(byte[] data, string? formatHint, PrintOptions opts)
    {
        if (data is null || data.Length == 0)
        {
            throw new ArgumentException("Empty label payload.");
        }

        var format = ResolveFormat(data, formatHint);
        var pages = Rasterize(data, format, opts.RenderDpi);
        try
        {
            var used = PrintBitmaps(pages, opts);
            return (used, pages.Count);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    /// <summary>Print a branded self-test label — used to verify a printer/page setup.</summary>
    public static (string Printer, int Pages) PrintTestLabel(PrintOptions opts)
    {
        using var label = BuildTestLabel();
        var used = PrintBitmaps(new[] { label }, opts);
        return (used, 1);
    }

    private static LabelFormat ResolveFormat(byte[] data, string? hint)
    {
        var sniffed = Detect(data);
        if (sniffed != LabelFormat.Unknown)
        {
            return sniffed;
        }

        return hint?.Trim().ToUpperInvariant() switch
        {
            "PDF" => LabelFormat.Pdf,
            "PNG" => LabelFormat.Png,
            "JPG" or "JPEG" => LabelFormat.Jpeg,
            _ => throw new InvalidOperationException("Unrecognised label format."),
        };
    }

    private static List<Bitmap> Rasterize(byte[] data, LabelFormat format, int dpi)
    {
        if (format == LabelFormat.Pdf)
        {
            return RenderPdf(data, dpi);
        }

        // PNG/JPEG: decode into a stream-independent bitmap.
        using var ms = new MemoryStream(data, writable: false);
        using var decoded = new Bitmap(ms);
        return new List<Bitmap> { new Bitmap(decoded) };
    }

    private static List<Bitmap> RenderPdf(byte[] pdf, int dpi)
    {
        var pages = new List<Bitmap>();
        foreach (var sk in Conversion.ToImages(pdf, options: new RenderOptions(Dpi: dpi)))
        {
            try
            {
                pages.Add(ToBitmap(sk, dpi));
            }
            finally
            {
                sk.Dispose();
            }
        }

        if (pages.Count == 0)
        {
            throw new InvalidOperationException("PDF produced no pages.");
        }
        return pages;
    }

    private static Bitmap ToBitmap(SKBitmap sk, int dpi)
    {
        using var image = SKImage.FromBitmap(sk);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        encoded.SaveTo(ms);
        ms.Position = 0;
        using var tmp = new Bitmap(ms);
        // Copy so the bitmap no longer references the (about-to-close) stream,
        // and stamp the render DPI so "actual size" printing knows the scale.
        var copy = new Bitmap(tmp);
        copy.SetResolution(dpi, dpi);
        return copy;
    }

    private static string PrintBitmaps(IReadOnlyList<Bitmap> pages, PrintOptions opts)
    {
        var settings = new PrinterSettings { Copies = (short)Math.Clamp(opts.Copies, 1, 10) };
        if (!string.IsNullOrWhiteSpace(opts.PrinterName))
        {
            settings.PrinterName = opts.PrinterName;
        }
        if (!settings.IsValid)
        {
            // Requested printer is gone/unset — fall back to the Windows default.
            settings.PrinterName = DefaultPrinter();
        }

        using var doc = new PrintDocument
        {
            DocumentName = "skuload label",
            PrinterSettings = settings,
            PrintController = new StandardPrintController(), // silent: no progress UI
            OriginAtMargins = true,
        };

        ApplyPaperSize(doc, settings, opts.PaperSizeName);
        doc.DefaultPageSettings.Margins = new Margins(
            MmToHundredths(opts.MarginLeftMm),
            MmToHundredths(opts.MarginRightMm),
            MmToHundredths(opts.MarginTopMm),
            MmToHundredths(opts.MarginBottomMm));

        var index = 0;
        var actualSize = opts.Fit == PrintFit.Actual;
        doc.PrintPage += (_, e) =>
        {
            var page = pages[index];
            var area = e.MarginBounds; // hundredths of an inch, inside the margins
            var (w, h) = actualSize
                ? NativeSize(page, opts.RenderDpi)
                : FitInto(page.Width, page.Height, area.Width, area.Height);

            var x = area.X + (area.Width - w) / 2;
            var y = area.Y + (area.Height - h) / 2;

            e.Graphics!.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(page, x, y, w, h);

            index++;
            e.HasMorePages = index < pages.Count;
        };

        doc.Print();
        return settings.PrinterName;
    }

    private static void ApplyPaperSize(PrintDocument doc, PrinterSettings settings, string? paperSizeName)
    {
        if (string.IsNullOrWhiteSpace(paperSizeName))
        {
            return;
        }

        foreach (PaperSize size in settings.PaperSizes)
        {
            if (string.Equals(size.PaperName, paperSizeName, StringComparison.OrdinalIgnoreCase))
            {
                doc.DefaultPageSettings.PaperSize = size;
                return;
            }
        }
        Logger.Warn($"Paper size '{paperSizeName}' not supported by printer; using its default.");
    }

    /// <summary>Native physical size of a bitmap in hundredths of an inch.</summary>
    private static (int Width, int Height) NativeSize(Bitmap page, int fallbackDpi)
    {
        var dpiX = page.HorizontalResolution > 1 ? page.HorizontalResolution : fallbackDpi;
        var dpiY = page.VerticalResolution > 1 ? page.VerticalResolution : fallbackDpi;
        return (
            Math.Max(1, (int)Math.Round(page.Width / dpiX * 100.0)),
            Math.Max(1, (int)Math.Round(page.Height / dpiY * 100.0)));
    }

    /// <summary>Largest size that fits inside the target while keeping aspect ratio.</summary>
    private static (int Width, int Height) FitInto(int srcW, int srcH, int boxW, int boxH)
    {
        if (srcW <= 0 || srcH <= 0)
        {
            return (boxW, boxH);
        }

        var scale = Math.Min((double)boxW / srcW, (double)boxH / srcH);
        return (Math.Max(1, (int)(srcW * scale)), Math.Max(1, (int)(srcH * scale)));
    }

    private static int MmToHundredths(double mm) => (int)Math.Round(mm / 25.4 * 100.0);

    private static Bitmap BuildTestLabel()
    {
        // 4x6" @ 150 DPI — the common shipping-label size.
        var bmp = new Bitmap(600, 900);
        bmp.SetResolution(150, 150);

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.White);

        var ink = Color.FromArgb(0x14, 0x16, 0x18);
        var signal = Color.FromArgb(0xC6, 0xF4, 0x32);
        using var inkBrush = new SolidBrush(ink);
        using var signalBrush = new SolidBrush(signal);

        // skuload mark: three stacked bars, middle one in Signal.
        const int bx = 48, by = 48, bw = 132, bh = 26, gap = 14;
        g.FillRectangle(inkBrush, bx, by, bw, bh);
        g.FillRectangle(signalBrush, bx, by + bh + gap, bw, bh);
        g.FillRectangle(inkBrush, bx, by + 2 * (bh + gap), bw, bh);

        using var wordFont = new Font("Segoe UI", 38, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("skuload", wordFont, inkBrush, 210, 70);
        var wordWidth = g.MeasureString("skuload", wordFont).Width;
        g.FillRectangle(signalBrush, 210 + wordWidth - 2, 70 + 36, 10, 10);

        using var titleFont = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("TEST LABEL", titleFont, inkBrush, 48, 230);

        using var monoFont = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString("printer self-test", monoFont, inkBrush, 48, 290);
        g.DrawString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), monoFont, inkBrush, 48, 326);

        // Decorative pseudo-barcode so scaling/orientation is obvious on paper.
        var rnd = 7;
        for (int x = 48; x < 552;)
        {
            rnd = (rnd * 1103515245 + 12345) & 0x7fffffff;
            var width = 2 + rnd % 6;
            if ((rnd >> 4 & 1) == 1)
            {
                g.FillRectangle(inkBrush, x, 460, width, 150);
            }
            x += width + 3;
        }

        using var pen = new Pen(ink, 3);
        g.DrawRectangle(pen, 12, 12, 600 - 24, 900 - 24);
        return bmp;
    }
}
