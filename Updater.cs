using Velopack;
using Velopack.Sources;

namespace Skuload.PrintBridge;

internal enum UpdateOutcome
{
    /// <summary>Running un-packaged (dev / raw exe) — updates don't apply.</summary>
    NotSupported,
    /// <summary>Already on the latest release.</summary>
    UpToDate,
    /// <summary>A newer release was downloaded and is staged to apply.</summary>
    Downloaded,
    /// <summary>The check or download failed (offline, GitHub down, etc.).</summary>
    Failed,
}

internal readonly record struct UpdateCheck(UpdateOutcome Outcome, string? Version);

/// <summary>
/// Thin wrapper over Velopack's <see cref="UpdateManager"/> pointed at the
/// GitHub Releases feed. Checks + downloads in the background; the caller
/// decides when to apply (we never interrupt an in-flight print — the update
/// lands on the next restart / Quit).
/// </summary>
internal sealed class Updater
{
    private readonly UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public Updater()
    {
        try
        {
            _mgr = new UpdateManager(new GithubSource(AppInfo.RepoUrl, null, false));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to init update manager", ex);
            _mgr = null;
        }
    }

    /// <summary>True when running as a Velopack-installed app (not dev/raw exe).</summary>
    public bool IsSupported => _mgr?.IsInstalled == true;

    /// <summary>The currently-installed version, or null when unpackaged.</summary>
    public string? CurrentVersion => _mgr?.CurrentVersion?.ToString();

    /// <summary>The staged update's version, once one has been downloaded.</summary>
    public string? PendingVersion => _pending?.TargetFullRelease?.Version?.ToString();

    public bool HasPendingUpdate => _pending is not null;

    /// <summary>Check GitHub and, if newer, download it silently. Never throws.</summary>
    public async Task<UpdateCheck> CheckAndDownloadAsync()
    {
        if (_mgr is null || !_mgr.IsInstalled)
        {
            return new UpdateCheck(UpdateOutcome.NotSupported, null);
        }

        try
        {
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return new UpdateCheck(UpdateOutcome.UpToDate, CurrentVersion);
            }

            await _mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info;
            var version = info.TargetFullRelease?.Version?.ToString();
            Logger.Info($"Update {version} downloaded and staged.");
            return new UpdateCheck(UpdateOutcome.Downloaded, version);
        }
        catch (Exception ex)
        {
            Logger.Error("Update check/download failed", ex);
            return new UpdateCheck(UpdateOutcome.Failed, null);
        }
    }

    /// <summary>Apply the staged update and relaunch the app. No-op if nothing staged.</summary>
    public void ApplyAndRestart()
    {
        if (_mgr is null || _pending is null)
        {
            return;
        }
        Logger.Info($"Applying update {PendingVersion} and restarting.");
        _mgr.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Apply the staged update quietly on process exit (used on normal Quit).</summary>
    public void ApplyOnExit()
    {
        if (_mgr is null || _pending is null)
        {
            return;
        }
        try
        {
            _mgr.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            Logger.Error("Scheduling update-on-exit failed", ex);
        }
    }
}
