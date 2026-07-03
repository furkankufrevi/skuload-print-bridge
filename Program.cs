using System.Windows.Forms;
using Velopack;

namespace Skuload.PrintBridge;

/// <summary>
/// Entry point. Runs a single-instance WinForms message loop whose only
/// surface is a system-tray icon (see <see cref="TrayApp"/>). All real work
/// happens in the localhost HTTP server it hosts.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // MUST be first: handles Velopack install/update/uninstall hooks and the
        // first-run bootstrap, then returns for a normal launch.
        VelopackApp.Build().Run();

        // Single instance: a second launch silently exits so the tray icon
        // and the localhost port are never duplicated.
        using var mutex = new Mutex(initiallyOwned: true, "skuload-print-bridge", out bool isNew);
        if (!isNew)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Application.Run(new TrayApp());
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }
}
