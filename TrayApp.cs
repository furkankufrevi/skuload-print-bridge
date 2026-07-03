using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Skuload.PrintBridge;

/// <summary>
/// The whole user-facing surface: a tray icon, a right-click menu (settings,
/// printer, test print, updates, autostart, logs, about, quit) and balloon
/// notifications. Owns the <see cref="BridgeServer"/> and <see cref="Updater"/>
/// lifecycles.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings;
    private readonly NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly Icon _appIcon;
    private readonly Updater _updater = new();
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private BridgeServer _server;
    private SettingsForm? _settingsForm;
    private AboutForm? _aboutForm;
    private string? _stagedVersion;

    public TrayApp()
    {
        // Fonts first, so any window we open renders on-brand.
        Fonts.Load();
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _settings = Settings.Load();

        // Keep the on-disk autostart state in sync with the saved preference.
        if (_settings.StartWithWindows != Autostart.IsEnabled())
        {
            Autostart.Set(_settings.StartWithWindows);
        }

        _trayIcon = LoadIcon("tray.ico") ?? SystemIcons.Application;
        _appIcon = LoadIcon("app.ico") ?? _trayIcon;
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = AppInfo.DisplayName,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _server = new BridgeServer(_settings);
        var port = StartServer();

        _tray.Text = Truncate($"{AppInfo.DisplayName} · 127.0.0.1:{port}", 63);
        _tray.ContextMenuStrip = BuildMenu();

        ShowFirstRunOrStatus(port);

        // Update: check shortly after launch, then every 6 hours.
        _updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdates(manual: false);
        _updateTimer.Start();
        _ = CheckForUpdates(manual: false);
    }

    private void ShowFirstRunOrStatus(int port)
    {
        if (!_settings.WelcomeShown)
        {
            _settings.WelcomeShown = true;
            _settings.Save();
            Notify(ToolTipIcon.Info, AppInfo.DisplayName,
                "Running in the tray. Open Settings to pick your printer.");
            return;
        }

        Notify(
            port > 0 ? ToolTipIcon.Info : ToolTipIcon.Error,
            AppInfo.DisplayName,
            port > 0 ? $"Running on 127.0.0.1:{port}" : "Server failed to start — see logs",
            port > 0 ? ToolTipIcon.Info : ToolTipIcon.Error);
    }

    private int StartServer()
    {
        try
        {
            return _server.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Server failed to start", ex);
            return 0;
        }
    }

    // ---- menu --------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Opening += (_, _) => Populate(menu);
        Populate(menu);
        return menu;
    }

    private void Populate(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var version = _updater.CurrentVersion ?? AppInfo.Version;
        menu.Items.Add(new ToolStripMenuItem($"{AppInfo.DisplayName} · v{version}") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem(
            _server.BoundPort > 0
                ? $"Listening · 127.0.0.1:{_server.BoundPort}"
                : "Not listening — see logs")
        { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        menu.Items.Add(BuildPrinterMenu());

        var test = new ToolStripMenuItem("Test print");
        test.Click += (_, _) => RunTestPrint();
        menu.Items.Add(test);

        menu.Items.Add(new ToolStripSeparator());

        // Update row: apply if staged, otherwise offer a manual check.
        if (_stagedVersion is not null)
        {
            var apply = new ToolStripMenuItem($"Restart & update to v{_stagedVersion}")
            {
                Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
            };
            apply.Click += (_, _) => _updater.ApplyAndRestart();
            menu.Items.Add(apply);
        }
        else
        {
            var check = new ToolStripMenuItem("Check for updates");
            check.Click += (_, _) => _ = CheckForUpdates(manual: true);
            menu.Items.Add(check);
        }

        var autostart = new ToolStripMenuItem("Start with Windows")
        {
            Checked = Autostart.IsEnabled(),
            CheckOnClick = true,
        };
        autostart.Click += (_, _) => ToggleAutostart(autostart.Checked);
        menu.Items.Add(autostart);

        menu.Items.Add(new ToolStripSeparator());

        var logs = new ToolStripMenuItem("Open logs");
        logs.Click += (_, _) => Open(File.Exists(Paths.LogFile) ? Paths.LogFile : Paths.DataDir);
        menu.Items.Add(logs);

        var data = new ToolStripMenuItem("Open data folder");
        data.Click += (_, _) => Open(Paths.DataDir);
        menu.Items.Add(data);

        var about = new ToolStripMenuItem("About…");
        about.Click += (_, _) => OpenAbout();
        menu.Items.Add(about);

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);
    }

    private ToolStripMenuItem BuildPrinterMenu()
    {
        var root = new ToolStripMenuItem("Printer");
        var current = string.IsNullOrWhiteSpace(_settings.PrinterName) ? null : _settings.PrinterName;

        var useDefault = new ToolStripMenuItem($"Default ({PrintService.DefaultPrinter()})")
        {
            Checked = current is null,
        };
        useDefault.Click += (_, _) => SelectPrinter(null);
        root.DropDownItems.Add(useDefault);
        root.DropDownItems.Add(new ToolStripSeparator());

        foreach (var printer in PrintService.InstalledPrinters())
        {
            var name = printer;
            var item = new ToolStripMenuItem(name)
            {
                Checked = string.Equals(name, current, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => SelectPrinter(name);
            root.DropDownItems.Add(item);
        }

        return root;
    }

    // ---- updates -----------------------------------------------------------

    private async Task CheckForUpdates(bool manual)
    {
        var result = await _updater.CheckAndDownloadAsync();
        _ui.Post(_ => OnUpdateResult(result, manual), null);
    }

    private void OnUpdateResult(UpdateCheck result, bool manual)
    {
        switch (result.Outcome)
        {
            case UpdateOutcome.Downloaded:
                _stagedVersion = result.Version;
                Notify(ToolTipIcon.Info, AppInfo.DisplayName,
                    $"Update v{result.Version} ready — choose \"Restart & update\" from the menu.");
                _aboutForm?.SetStatus($"Update v{result.Version} downloaded. Restart to apply.");
                break;
            case UpdateOutcome.UpToDate:
                _aboutForm?.SetStatus($"Up to date (v{result.Version ?? AppInfo.Version}).");
                if (manual)
                {
                    Notify(ToolTipIcon.Info, AppInfo.DisplayName, "You're on the latest version.");
                }
                break;
            case UpdateOutcome.Failed:
                _aboutForm?.SetStatus("Update check failed — check your connection.");
                if (manual)
                {
                    Notify(ToolTipIcon.Warning, AppInfo.DisplayName, "Couldn't check for updates.");
                }
                break;
            case UpdateOutcome.NotSupported:
                _aboutForm?.SetStatus("Updates apply to the installed app only.");
                break;
        }
    }

    // ---- actions -----------------------------------------------------------

    private void SelectPrinter(string? name)
    {
        _settings.PrinterName = name;
        _settings.Save();
        Logger.Info($"Printer set to '{name ?? "(default)"}'.");
    }

    private void ToggleAutostart(bool enabled)
    {
        Autostart.Set(enabled);
        _settings.StartWithWindows = enabled;
        _settings.Save();
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            RestoreWindow(_settingsForm);
            return;
        }

        _settingsForm = new SettingsForm(_settings, _appIcon) { OnSaved = HandleSettingsSaved };
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenAbout()
    {
        if (_aboutForm is { IsDisposed: false })
        {
            RestoreWindow(_aboutForm);
            return;
        }

        _aboutForm = new AboutForm(_appIcon, () => CheckForUpdates(manual: true));
        _aboutForm.FormClosed += (_, _) => _aboutForm = null;
        _aboutForm.Show();
        _aboutForm.Activate();
    }

    private static void RestoreWindow(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }
        form.Activate();
        form.BringToFront();
    }

    /// <summary>After settings are saved, rebind the server if the port changed.</summary>
    private void HandleSettingsSaved()
    {
        if (_server.BoundPort != _settings.Port)
        {
            RestartServer();
        }
    }

    private void RestartServer()
    {
        try
        {
            _server.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("Stopping server before restart failed", ex);
        }

        _server = new BridgeServer(_settings);
        var port = StartServer();
        _tray.Text = Truncate($"{AppInfo.DisplayName} · 127.0.0.1:{port}", 63);
        Notify(
            port > 0 ? ToolTipIcon.Info : ToolTipIcon.Error,
            AppInfo.DisplayName,
            port > 0 ? $"Restarted on 127.0.0.1:{port}" : "Restart failed — see logs");
    }

    private void RunTestPrint()
    {
        var opts = PrintOptions.FromSettings(_settings);
        Task.Run(() =>
        {
            try
            {
                var (used, _) = PrintService.PrintTestLabel(opts);
                Logger.Info($"Test label sent to '{used}'.");
                Notify(ToolTipIcon.Info, "Test print", $"Sent to {used}");
            }
            catch (Exception ex)
            {
                Logger.Error("Test print failed", ex);
                Notify(ToolTipIcon.Error, "Test print failed", ex.Message);
            }
        });
    }

    private void Notify(ToolTipIcon icon, string title, string text, ToolTipIcon? fallback = null)
    {
        try
        {
            _tray.ShowBalloonTip(3000, title, Truncate(text, 200), fallback ?? icon);
        }
        catch
        {
            // Ignore notification failures.
        }
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open '{path}'", ex);
        }
    }

    private void ExitApp()
    {
        try
        {
            // If an update is staged, let it apply quietly after we exit.
            if (_updater.HasPendingUpdate)
            {
                _updater.ApplyOnExit();
            }
            _tray.Visible = false;
            _server.Dispose();
        }
        finally
        {
            ExitThread();
        }
    }

    private static Icon? LoadIcon(string logicalName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(logicalName);
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load icon {logicalName}", ex);
            return null;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Dispose();
            _settingsForm?.Dispose();
            _aboutForm?.Dispose();
            _tray.Dispose();
            _trayIcon.Dispose();
            if (!ReferenceEquals(_appIcon, _trayIcon))
            {
                _appIcon.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
