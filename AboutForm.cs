using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Skuload.PrintBridge;

/// <summary>
/// The About window: wordmark, version, a "Check for updates" action, and links
/// to the logs + GitHub releases. The update check is injected so this form
/// stays decoupled from <see cref="Updater"/>.
/// </summary>
internal sealed class AboutForm : Form
{
    private readonly Branding.Palette _p = Branding.Current();
    private readonly Func<Task>? _checkForUpdates;
    private Label? _updateStatus;
    private Button? _checkButton;

    public AboutForm(Icon? icon, Func<Task>? checkForUpdates)
    {
        _checkForUpdates = checkForUpdates;

        Text = "About skuload print bridge";
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
        ClientSize = new Size(400, 300);

        BuildLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BrandUi.ApplyTitleBarTheme(this, _p.IsDark);
    }

    private void BuildLayout()
    {
        var wordmark = new WordmarkControl(_p.Text, 30f) { Location = new Point(28, 28) };

        var tag = new Label
        {
            Text = "PRINT BRIDGE",
            AutoSize = true,
            Font = Fonts.Mono(11f),
            ForeColor = _p.TextMute,
            Location = new Point(30, 70),
        };
        var version = new Label
        {
            Text = $"Version {AppInfo.Version}",
            AutoSize = true,
            Font = Fonts.Mono(11f),
            ForeColor = _p.TextFaint,
            Location = new Point(30, 92),
        };
        var blurb = new Label
        {
            Text = "Prints shipping labels straight to a printer from the skuload web app.",
            AutoSize = false,
            Size = new Size(344, 36),
            Font = Fonts.Display(12f),
            ForeColor = _p.TextSoft,
            Location = new Point(30, 122),
        };

        _checkButton = BrandUi.PrimaryButton("Check for updates");
        _checkButton.Width = 168;
        _checkButton.Location = new Point(30, 172);
        _checkButton.Click += async (_, _) => await RunCheck();

        _updateStatus = new Label
        {
            Text = "",
            AutoSize = true,
            Font = Fonts.Display(11.5f),
            ForeColor = _p.TextMute,
            Location = new Point(30, 214),
            MaximumSize = new Size(344, 0),
        };

        var logs = LinkButton("Open logs", () => Open(File.Exists(Paths.LogFile) ? Paths.LogFile : Paths.DataDir));
        logs.Location = new Point(30, 244);
        var releases = LinkButton("Releases ↗", () => Open(AppInfo.ReleasesUrl));
        releases.Location = new Point(124, 244);

        var copyright = new Label
        {
            Text = "© skuload",
            AutoSize = true,
            Font = Fonts.Mono(10f),
            ForeColor = _p.TextFaint,
            Location = new Point(30, 272),
        };

        Controls.Add(wordmark);
        Controls.Add(tag);
        Controls.Add(version);
        Controls.Add(blurb);
        Controls.Add(_checkButton);
        Controls.Add(_updateStatus);
        Controls.Add(logs);
        Controls.Add(releases);
        Controls.Add(copyright);
    }

    private LinkLabel LinkButton(string text, Action onClick)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Font = Fonts.Display(12f),
            LinkColor = Branding.SignalText,
            ActiveLinkColor = Branding.Signal,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        link.LinkClicked += (_, _) => onClick();
        return link;
    }

    private async Task RunCheck()
    {
        if (_checkForUpdates is null || _checkButton is null || _updateStatus is null)
        {
            SetStatus("Updates aren't available in this build.");
            return;
        }

        _checkButton.Enabled = false;
        SetStatus("Checking…");
        try
        {
            await _checkForUpdates();
        }
        catch (Exception ex)
        {
            Logger.Error("Manual update check failed", ex);
            SetStatus("Update check failed.");
        }
        finally
        {
            _checkButton.Enabled = true;
        }
    }

    /// <summary>Update the status line (safe to call from any thread).</summary>
    public void SetStatus(string text)
    {
        if (_updateStatus is null)
        {
            return;
        }
        if (_updateStatus.InvokeRequired)
        {
            _updateStatus.BeginInvoke(() => _updateStatus.Text = text);
        }
        else
        {
            _updateStatus.Text = text;
        }
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open '{target}'", ex);
        }
    }
}
