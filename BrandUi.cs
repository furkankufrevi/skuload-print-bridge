using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Skuload.PrintBridge;

/// <summary>
/// Shared skuload-branded WinForms building blocks: the wordmark, themed
/// buttons, an immersive dark title bar, and a helper to tint standard inputs.
/// Keeps <see cref="SettingsForm"/> and <see cref="AboutForm"/> on one look.
/// </summary>
internal static class BrandUi
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Paint the window title bar dark to match a dark theme (Win10 2004+).</summary>
    public static void ApplyTitleBarTheme(Form form, bool dark)
    {
        try
        {
            var on = dark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on early 20H1 builds).
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // Non-fatal: just leaves a light title bar on old Windows.
        }
    }

    /// <summary>A Signal-filled primary button (Ink text), flat and on-brand.</summary>
    public static Button PrimaryButton(string text)
    {
        var b = BaseButton(text);
        b.BackColor = Branding.Signal;
        b.ForeColor = Branding.Ink;
        b.Font = Fonts.Display(13f, Fonts.Weight.Medium);
        b.FlatAppearance.MouseOverBackColor = Branding.SignalHover;
        b.FlatAppearance.MouseDownBackColor = Branding.SignalText;
        return b;
    }

    /// <summary>A secondary button: elevated surface + line border.</summary>
    public static Button SecondaryButton(string text, Branding.Palette p)
    {
        var b = BaseButton(text);
        b.BackColor = p.BgElev;
        b.ForeColor = p.Text;
        b.Font = Fonts.Display(13f, Fonts.Weight.Medium);
        b.FlatAppearance.BorderColor = p.Line2;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = p.BgSunk;
        return b;
    }

    private static Button BaseButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 36,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
    };

    /// <summary>Tint a combo/numeric input to the theme (best-effort; WinForms limits chrome).</summary>
    public static void StyleInput(Control input, Branding.Palette p)
    {
        input.BackColor = p.BgElev;
        input.ForeColor = p.Text;
        if (input is ComboBox cb)
        {
            cb.FlatStyle = FlatStyle.Flat;
        }
        if (input is NumericUpDown nud)
        {
            nud.BorderStyle = BorderStyle.FixedSingle;
        }
    }

    /// <summary>A Geist-Mono uppercase section caption (like the web's card headers).</summary>
    public static Label SectionLabel(string text, Branding.Palette p) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        Font = Fonts.Mono(11f),
        ForeColor = p.TextMute,
        Margin = new Padding(0, 2, 0, 2),
    };

    /// <summary>A plain themed field label.</summary>
    public static Label FieldLabel(string text, Branding.Palette p) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Fonts.Display(12.5f),
        ForeColor = p.TextSoft,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 9, 8, 8),
    };
}

/// <summary>
/// The skuload wordmark: <c>skuload</c> in Geist SemiBold with a Signal-coloured
/// period. Owner-drawn so the period is always the Signal accent.
/// </summary>
internal sealed class WordmarkControl : Control
{
    private readonly float _sizePx;
    private Color _wordColor;

    public WordmarkControl(Color wordColor, float sizePx = 22f)
    {
        _sizePx = sizePx;
        _wordColor = wordColor;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = (int)(sizePx * 1.4f);
        Width = (int)(sizePx * 6.2f);
    }

    public void SetWordColor(Color c)
    {
        _wordColor = c;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var font = Fonts.Display(_sizePx, Fonts.Weight.SemiBold);
        const string word = "skuload";
        var wordSize = g.MeasureString(word, font);
        var y = (Height - wordSize.Height) / 2f;

        using var wordBrush = new SolidBrush(_wordColor);
        using var dotBrush = new SolidBrush(Branding.Signal);
        g.DrawString(word, font, wordBrush, 0, y);
        g.DrawString(".", font, dotBrush, wordSize.Width - _sizePx * 0.18f, y);
    }
}
