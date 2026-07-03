using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Skuload.PrintBridge;

/// <summary>
/// The configuration window: port, printer, paper size, margins, copies, render
/// DPI and scaling. Edits the shared <see cref="Settings"/> instance and
/// persists it on Save. A "Test print" button runs the current (possibly
/// unsaved) selection so the operator can dial in the page setup first. Styled
/// with the skuload palette + Geist, following the current Windows theme.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly Branding.Palette _p = Branding.Current();

    /// <summary>Invoked after Save persists — lets the tray restart the server if the port changed.</summary>
    public Action? OnSaved { get; set; }

    private readonly NumericUpDown _port = new()
    {
        Minimum = 1024,
        Maximum = 65535,
        Width = 100,
        ThousandsSeparator = false,
    };
    private readonly ComboBox _printer = NewCombo();
    private readonly ComboBox _paper = NewCombo();
    private readonly NumericUpDown _marginLeft = NewMm();
    private readonly NumericUpDown _marginTop = NewMm();
    private readonly NumericUpDown _marginRight = NewMm();
    private readonly NumericUpDown _marginBottom = NewMm();
    private readonly NumericUpDown _copies = new()
    {
        Minimum = 1,
        Maximum = 10,
        Width = 80,
    };
    private readonly ComboBox _dpi = NewCombo();
    private readonly ComboBox _scaling = NewCombo();
    private Label? _savedHint;

    private bool _loading;

    public SettingsForm(Settings settings, Icon? icon)
    {
        _settings = settings;

        Text = "skuload print bridge";
        if (icon is not null)
        {
            Icon = icon;
        }
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Fonts.Display(12.5f);
        BackColor = _p.Bg;
        ForeColor = _p.Text;
        ClientSize = new Size(460, 520);

        BuildLayout();
        LoadFromSettings();

        _printer.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
            {
                RepopulatePaper(SelectedValue(_printer), SelectedValue(_paper));
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BrandUi.ApplyTitleBarTheme(this, _p.IsDark);
    }

    // ---- layout ------------------------------------------------------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = _p.Bg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = _p.BgSunk, Padding = new Padding(16, 12, 16, 12) };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Line);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var wordmark = new WordmarkControl(_p.Text, 20f) { Location = new Point(16, 12) };
        var tag = new Label
        {
            Text = "PRINT BRIDGE",
            AutoSize = true,
            Font = Fonts.Mono(10f),
            ForeColor = _p.TextMute,
            Location = new Point(18, 42),
        };

        var version = new Label
        {
            Text = "v" + AppVersion(),
            AutoSize = true,
            Font = Fonts.Mono(10f),
            ForeColor = _p.TextFaint,
            Anchor = AnchorStyles.Right,
        };
        var status = new Label
        {
            Text = $"listening · 127.0.0.1:{_settings.Port}",
            AutoSize = true,
            Font = Fonts.Mono(10f),
            ForeColor = _p.TextSoft,
            Anchor = AnchorStyles.Right,
        };
        var dot = new SignalDot { Size = new Size(8, 8), Anchor = AnchorStyles.Right };

        header.Controls.Add(wordmark);
        header.Controls.Add(tag);
        header.Controls.Add(version);
        header.Controls.Add(status);
        header.Controls.Add(dot);

        // Right-align the status cluster on resize.
        void Reflow()
        {
            version.Location = new Point(header.Width - 16 - version.Width, 14);
            status.Location = new Point(header.Width - 16 - status.Width, 40);
            dot.Location = new Point(status.Left - 14, status.Top + 4);
        }
        header.Resize += (_, _) => Reflow();
        header.HandleCreated += (_, _) => Reflow();
        return header;
    }

    private Control BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 8),
            ColumnCount = 2,
            BackColor = _p.Bg,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _dpi.Items.AddRange(new object[]
        {
            new Choice("150 dpi", "150"),
            new Choice("203 dpi (thermal)", "203"),
            new Choice("300 dpi", "300"),
            new Choice("600 dpi", "600"),
        });
        _scaling.Items.AddRange(new object[]
        {
            new Choice("Fit to page", "fit"),
            new Choice("Actual size", "actual"),
        });

        foreach (var c in new Control[] { _port, _printer, _paper, _copies, _dpi, _scaling,
            _marginLeft, _marginTop, _marginRight, _marginBottom })
        {
            BrandUi.StyleInput(c, _p);
        }

        AddSection(body, "Connection");
        AddRow(body, "Port", _port);
        AddSection(body, "Printer & page");
        AddRow(body, "Printer", _printer);
        AddRow(body, "Paper size", _paper);
        AddRow(body, "Margins (mm)", BuildMarginsPanel());
        AddRow(body, "Copies", _copies);
        AddRow(body, "Render DPI", _dpi);
        AddRow(body, "Scaling", _scaling);
        return body;
    }

    private Control BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Fill, Height = 56, BackColor = _p.BgSunk, Padding = new Padding(16, 10, 16, 10) };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(_p.Line);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        var test = BrandUi.SecondaryButton("Test print", _p);
        test.Width = 104;
        test.Location = new Point(16, 10);
        test.Click += (_, _) => RunTestPrint();

        var save = BrandUi.PrimaryButton("Save");
        save.Width = 92;
        save.Click += (_, _) => { SaveToSettings(); Close(); };

        var close = BrandUi.SecondaryButton("Close", _p);
        close.Width = 84;
        close.DialogResult = DialogResult.Cancel;

        _savedHint = new Label
        {
            Text = "",
            AutoSize = true,
            Font = Fonts.Display(12f),
            ForeColor = Branding.Success,
        };

        footer.Controls.Add(test);
        footer.Controls.Add(save);
        footer.Controls.Add(close);
        footer.Controls.Add(_savedHint);

        void Reflow()
        {
            close.Location = new Point(footer.Width - 16 - close.Width, 10);
            save.Location = new Point(close.Left - 8 - save.Width, 10);
            _savedHint.Location = new Point(test.Right + 12, 18);
        }
        footer.Resize += (_, _) => Reflow();
        footer.HandleCreated += (_, _) => Reflow();

        CancelButton = close;
        return footer;
    }

    private Control BuildMarginsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            BackColor = _p.Bg,
        };
        panel.Controls.Add(Labelled("L", _marginLeft));
        panel.Controls.Add(Labelled("T", _marginTop));
        panel.Controls.Add(Labelled("R", _marginRight));
        panel.Controls.Add(Labelled("B", _marginBottom));
        return panel;
    }

    private Control Labelled(string text, Control control)
    {
        var holder = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 8, 0),
            BackColor = _p.Bg,
        };
        holder.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = Fonts.Mono(10f),
            Margin = new Padding(0, 8, 3, 0),
            ForeColor = _p.TextMute,
        });
        holder.Controls.Add(control);
        return holder;
    }

    private void AddSection(TableLayoutPanel body, string title)
    {
        var row = body.RowCount;
        var label = BrandUi.SectionLabel(title, _p);
        label.Margin = new Padding(0, row == 0 ? 2 : 14, 0, 4);
        body.Controls.Add(label, 0, row);
        body.SetColumnSpan(label, 2);
        body.RowCount = row + 1;
    }

    private void AddRow(TableLayoutPanel body, string label, Control control)
    {
        var row = body.RowCount;
        body.Controls.Add(BrandUi.FieldLabel(label, _p), 0, row);
        control.Margin = new Padding(0, 5, 0, 5);
        if (control is ComboBox)
        {
            control.Dock = DockStyle.Fill;
        }
        body.Controls.Add(control, 1, row);
        body.RowCount = row + 1;
    }

    // ---- state <-> controls ------------------------------------------------

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            _port.Value = Math.Clamp(_settings.Port, (int)_port.Minimum, (int)_port.Maximum);

            _printer.Items.Clear();
            _printer.Items.Add(new Choice($"Default ({PrintService.DefaultPrinter()})", null));
            foreach (var name in PrintService.InstalledPrinters())
            {
                _printer.Items.Add(new Choice(name, name));
            }
            SelectValue(_printer, string.IsNullOrWhiteSpace(_settings.PrinterName) ? null : _settings.PrinterName);

            RepopulatePaper(SelectedValue(_printer), _settings.PaperSizeName);

            _marginLeft.Value = Clamp(_settings.MarginLeftMm);
            _marginTop.Value = Clamp(_settings.MarginTopMm);
            _marginRight.Value = Clamp(_settings.MarginRightMm);
            _marginBottom.Value = Clamp(_settings.MarginBottomMm);
            _copies.Value = Math.Clamp(_settings.Copies, 1, 10);
            SelectValue(_dpi, _settings.RenderDpi.ToString());
            SelectValue(_scaling, _settings.Scaling);
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveToSettings()
    {
        _settings.Port = (int)_port.Value;
        _settings.PrinterName = SelectedValue(_printer);
        _settings.PaperSizeName = SelectedValue(_paper);
        _settings.MarginLeftMm = (double)_marginLeft.Value;
        _settings.MarginTopMm = (double)_marginTop.Value;
        _settings.MarginRightMm = (double)_marginRight.Value;
        _settings.MarginBottomMm = (double)_marginBottom.Value;
        _settings.Copies = (int)_copies.Value;
        _settings.RenderDpi = int.TryParse(SelectedValue(_dpi), out var dpi) ? dpi : 300;
        _settings.Scaling = SelectedValue(_scaling) ?? "fit";
        _settings.Save();
        Logger.Info("Settings saved from the settings window.");
        OnSaved?.Invoke();
    }

    private PrintOptions CurrentOptions()
    {
        return new PrintOptions
        {
            PrinterName = SelectedValue(_printer),
            PaperSizeName = SelectedValue(_paper),
            MarginLeftMm = (double)_marginLeft.Value,
            MarginTopMm = (double)_marginTop.Value,
            MarginRightMm = (double)_marginRight.Value,
            MarginBottomMm = (double)_marginBottom.Value,
            Copies = (int)_copies.Value,
            RenderDpi = int.TryParse(SelectedValue(_dpi), out var dpi) ? dpi : 300,
            Fit = string.Equals(SelectedValue(_scaling), "actual", StringComparison.OrdinalIgnoreCase)
                ? PrintFit.Actual
                : PrintFit.Fit,
        };
    }

    private void RunTestPrint()
    {
        var opts = CurrentOptions();
        try
        {
            var (used, _) = PrintService.PrintTestLabel(opts);
            if (_savedHint is not null)
            {
                _savedHint.Text = $"Sent to {used}";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Test print (settings) failed", ex);
            MessageBox.Show(this, ex.Message, "Test print failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RepopulatePaper(string? printerName, string? selectName)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _paper.Items.Clear();
            _paper.Items.Add(new Choice("Printer default", null));
            foreach (var p in PrintService.PaperSizes(printerName))
            {
                _paper.Items.Add(new Choice($"{p.Name} — {p.WidthMm:0.#}×{p.HeightMm:0.#} mm", p.Name));
            }
            SelectValue(_paper, string.IsNullOrWhiteSpace(selectName) ? null : selectName);
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // ---- helpers -----------------------------------------------------------

    private static string AppVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static ComboBox NewCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300,
    };

    private static NumericUpDown NewMm() => new()
    {
        Minimum = 0,
        Maximum = 100,
        DecimalPlaces = 1,
        Increment = 0.5m,
        Width = 58,
    };

    private static decimal Clamp(double mm) => (decimal)Math.Clamp(mm, 0, 100);

    private static string? SelectedValue(ComboBox combo) => (combo.SelectedItem as Choice)?.Value;

    private static void SelectValue(ComboBox combo, string? value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is Choice c &&
                string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    /// <summary>A combo entry: display text + the underlying value (null = "default").</summary>
    private sealed record Choice(string Text, string? Value)
    {
        public override string ToString() => Text;
    }
}

/// <summary>A small Signal-coloured status dot.</summary>
internal sealed class SignalDot : Control
{
    public SignalDot()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Branding.Signal);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
    }
}
