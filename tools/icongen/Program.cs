using SkiaSharp;

// Generates the skuload three-bar mark as multi-resolution .ico files.
//
//   assets/app.ico  — brand mark: Ink top/bottom bars, Signal middle. For the
//                     exe, Start menu, installer (shown on light surfaces).
//   assets/tray.ico — neutral top/bottom bars + Signal middle, so it reads on
//                     both light and dark Windows taskbars (Ink bars would
//                     vanish on a dark taskbar).
//
// Mark spec (design.md §1), on a 24u grid:
//   bar height 4u, gutter 2u, corner radius 1.2u
//   top/bottom bars: width 18u at x=3   middle bar: width 14u at x=7
//   (all three share the right edge at x=21 — the middle bar's left indent is
//    the intentional "motion through the rack" step.)

var outDir = args.Length > 0 ? args[0] : "assets";
Directory.CreateDirectory(outDir);

var ink = new SKColor(0x14, 0x16, 0x18);       // Ink-1000
var signal = new SKColor(0xC6, 0xF4, 0x32);    // Signal
var neutral = new SKColor(0x9A, 0xA0, 0xA6);   // reads on light + dark taskbars

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

WriteIco(Path.Combine(outDir, "app.ico"), sizes, top: ink, mid: signal, bottom: ink);
WriteIco(Path.Combine(outDir, "tray.ico"), sizes, top: neutral, mid: signal, bottom: neutral);

Console.WriteLine($"Wrote app.ico + tray.ico to {Path.GetFullPath(outDir)}");

static void WriteIco(string path, int[] sizes, SKColor top, SKColor mid, SKColor bottom)
{
    var pngs = sizes.Select(s => RenderPng(s, top, mid, bottom)).ToArray();
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);

    // ICONDIR
    w.Write((ushort)0);              // reserved
    w.Write((ushort)1);              // type: 1 = icon
    w.Write((ushort)sizes.Length);   // image count

    // ICONDIRENTRY[] — image data follows the directory.
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        var s = sizes[i];
        w.Write((byte)(s >= 256 ? 0 : s)); // width  (0 = 256)
        w.Write((byte)(s >= 256 ? 0 : s)); // height (0 = 256)
        w.Write((byte)0);                  // palette count
        w.Write((byte)0);                  // reserved
        w.Write((ushort)1);                // colour planes
        w.Write((ushort)32);               // bits per pixel
        w.Write((uint)pngs[i].Length);     // bytes in resource
        w.Write((uint)offset);             // offset from file start
        offset += pngs[i].Length;
    }

    foreach (var png in pngs)
    {
        w.Write(png);
    }
}

static byte[] RenderPng(int size, SKColor top, SKColor mid, SKColor bottom)
{
    using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bmp);
    canvas.Clear(SKColors.Transparent);

    var u = size / 24f;              // one grid unit in pixels
    var r = 1.2f * u;               // corner radius
    var h = 4f * u;                 // bar height

    Bar(canvas, 3 * u, 4 * u, 18 * u, h, r, top);      // top    (y 4..8)
    Bar(canvas, 7 * u, 10 * u, 14 * u, h, r, mid);     // middle (y 10..14)
    Bar(canvas, 3 * u, 16 * u, 18 * u, h, r, bottom);  // bottom (y 16..20)

    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static void Bar(SKCanvas canvas, float x, float y, float w, float h, float r, SKColor color)
{
    using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
    // Clamp the radius so tiny sizes don't over-round into a pill.
    var rr = Math.Min(r, h / 2f);
    canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), rr, rr, paint);
}
