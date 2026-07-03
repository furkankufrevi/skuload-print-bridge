# skuload print bridge

A tiny Windows tray app that lets the skuload web app **print shipping labels
directly to a physical printer** — no browser print dialog, no manual "open PDF
then Ctrl+P". When the operator clicks **Get label & print** in the web UI, the
label is POSTed to this bridge running on `127.0.0.1` and comes straight out of
the chosen printer.

```
┌────────────────────────┐         ┌──────────────────────────────────┐
│  skuload web (browser) │         │  Windows PC                      │
│                        │  HTTP   │  skuload-print-bridge.exe (tray) │
│  "Get label & print" ──┼────────▶│  POST /print { labelBase64,… }   │
│                        │ 127.0.0.1│           │                      │
│   ◀── { ok, jobId } ───┼─────────┤           ▼  silent print        │
└────────────────────────┘         │      ┌──────────┐                │
                                    │      │ printer  │  ▓▓▓ label ▓▓▓ │
                                    │      └──────────┘                │
                                    └──────────────────────────────────┘
```

## What it does

- Runs in the system tray; starts with Windows (optional).
- Hosts a **localhost-only** HTTP server (default `http://127.0.0.1:7654`).
- Prints **PDF or PNG/JPG** labels silently to the selected (or default) printer.
  PDFs are rasterised with pdfium so any printer works.
- A **Settings window** (double-click the tray icon, or right-click → Settings…)
  to configure: **port**, printer, **paper size**, **page margins (mm)**, copies,
  render DPI, and scaling (**fit to page** / **actual size**) — with a Test print
  button. Changing the port restarts the server on the new port automatically.

## HTTP API

All endpoints are open CORS (any origin is reflected) and answer the Chrome
Private Network Access preflight. The server only listens on loopback, so it
isn't reachable from the network regardless.

### `GET /ping`
Liveness + capabilities. The web app calls this to detect whether the bridge is
installed/running and to list printers.

```json
{
  "ok": true,
  "name": "skuload-print-bridge",
  "version": "1.0.0",
  "status": "ready",
  "port": 7654,
  "printers": ["Zebra ZD420", "Microsoft Print to PDF"],
  "defaultPrinter": "Zebra ZD420",
  "selectedPrinter": "Zebra ZD420"
}
```

### `GET /printers`
`{ "ok": true, "printers": ["…"] }`

### `POST /print`
```json
{
  "labelBase64": "JVBERi0xLjQ…",   // raw base64, or a data: URL — both accepted
  "format": "PDF",                  // optional hint; the bytes are sniffed anyway
  "printer": "Zebra ZD420",         // optional; falls back to the selected/default printer
  "copies": 1                        // optional; defaults to the configured value
}
```
→ `{ "ok": true, "jobId": "…", "printer": "Zebra ZD420", "pages": 1 }`
On failure → HTTP 4xx/5xx with `{ "ok": false, "error": "…" }`.

## Settings

Stored at `%APPDATA%\skuload\print-bridge\settings.json` (also editable from the
tray menu). Logs sit next to it in `bridge.log`.

```json
{
  "port": 7654,
  "printerName": null,                 // null = Windows default printer
  "copies": 1,
  "renderDpi": 300,                    // PDF rasterisation DPI (203 ≈ thermal)
  "paperSizeName": null,               // null = printer's default page size, e.g. "4 x 6"
  "marginLeftMm": 0,
  "marginTopMm": 0,
  "marginRightMm": 0,
  "marginBottomMm": 0,
  "scaling": "fit",                    // "fit" (scale to page) | "actual" (native size)
  "startWithWindows": false
}
```

All of these are editable from the Settings window — you don't need to touch the
file by hand. Paper size and margins also apply to the test print, so you can
dial in the page setup before going live.

## Security model

- Binds to `127.0.0.1` only — never reachable from the network.
- Open CORS (any origin) — so the live web app works with no per-origin setup.
  The loopback-only bind is what keeps it private.
- Runs as the current user (`asInvoker`) — no admin/UAC.

## Install

Download **`Setup.exe`** from the latest release:
<https://github.com/furkankufrevi/skuload-print-bridge/releases/latest>

Run it — it installs per-user to `%LocalAppData%\skuloadPrintBridge` (no admin),
adds a Start-menu entry, and launches the app into the system tray.

> **SmartScreen:** the build is not code-signed yet, so Windows may show
> "Windows protected your PC". Click **More info → Run anyway**. (A signing step
> is stubbed in the release workflow for when a certificate is available.)

## Auto-update

The app is packaged with [Velopack](https://velopack.io) and uses **GitHub
Releases as its update feed**. It checks shortly after launch and every 6 hours;
a newer release downloads silently in the background. When one is staged you get
a tray notification and a **"Restart & update to vX"** menu item (updates also
apply on the next normal Quit — an in-flight print is never interrupted). You can
also trigger a check from the tray ("Check for updates") or the About window.

### Cutting a release

Releases are built by GitHub Actions (`.github/workflows/release.yml`) on
`windows-latest`. Push a SemVer tag:

```bash
git tag v1.0.1
git push origin v1.0.1
```

CI publishes the app, runs `vpk pack` (full + delta packages), and uploads
`Setup.exe` + the update feed to the GitHub release. Installed clients pick it up
on their next check. (You can also run the workflow manually from the Actions tab
and pass the version.)

## Build (local / dev)

Produces a self-contained `win-x64` **folder** (Velopack manages the unpacked
app; no .NET install needed on the target PC).

**On Windows:**
```powershell
.\build.ps1              # publish -> publish\
.\build.ps1 -Version 1.2.3  # publish + vpk pack -> releases\
```

**On macOS/Linux (dev):**
```bash
./build.sh               # publish -> publish/
```
> Requires the **official** .NET 8 SDK. Homebrew's `dotnet@8` is trimmed and
> lacks the Windows Desktop targets — install a local official SDK with:
> `curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"`
>
> Building a real `Setup.exe` is best done in CI on Windows; local `vpk pack`
> for a Windows target is best-effort.

The brand icons (`assets/app.ico`, `assets/tray.ico`) are regenerated from the
mark with `dotnet run --project tools/icongen` (writes to `assets/`).

## Run

Double-click the tray icon to open **Settings** (port, printer, paper size,
margins, copies, DPI, scaling). Right-click for the menu: Settings…, quick
printer pick, test print, check for updates, "Start with Windows", logs,
About…, quit.

## Web integration

The frontend calls the bridge through `src/lib/printBridge.ts` in the
`frontend` repo (`pingBridge()` / `printLabel()`). The shipment label flow first
pings the bridge; if it's running, the label prints directly, otherwise the UI
falls back to opening the label in a new tab.

## Project layout

| File | Role |
|------|------|
| `Program.cs` | Velopack bootstrap + single-instance entry; starts the tray loop. |
| `TrayApp.cs` | Tray icon, menu, owns the server / settings / updater lifecycles. |
| `SettingsForm.cs` | Settings window: port, printer, paper, margins, DPI, scaling. |
| `AboutForm.cs` | About window: version, check-for-updates, links. |
| `Updater.cs` | Velopack update check/download/apply over the GitHub feed. |
| `Branding.cs` / `Fonts.cs` / `BrandUi.cs` | skuload palette, embedded Geist, shared UI. |
| `BridgeServer.cs` | localhost HTTP server, routes, CORS/PNA. |
| `PrintService.cs` | PDF→raster + silent printing; paper/margins/fit; self-test label. |
| `PrintOptions.cs` | Per-job print parameters, built from `Settings`. |
| `Settings.cs` / `AppInfo.cs` | JSON config under `%APPDATA%`; app identity/links. |
| `Autostart.cs` | HKCU Run-key toggle. |
| `Logger.cs` / `Paths.cs` | File logging + on-disk locations. |
| `assets/` · `tools/icongen/` | Brand icons + fonts, and the icon generator. |
| `.github/workflows/` | `ci.yml` (build gate) + `release.yml` (tag → Setup.exe). |
