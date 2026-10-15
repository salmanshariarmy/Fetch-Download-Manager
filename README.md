# Fetch — Windows Download Manager (0.4.0 Browser + Optional Media Upgrade)

A source-code download manager for **Windows 10/11 x64**. Paste a direct HTTP(S) file URL, start a segmented download, pause/resume it, and browse download history with an organized desktop interface. Ranged downloads use up to eight connections.

> **Scope:** This is an incremental desktop and Chrome/Edge integration upgrade, **not** the complete
> IDM-scale application in `docs/Original-full-specification.txt`. In particular,
> the C++/SQLite engine, full authenticated browser capture, full native video sniffing,
> site grabber, signed MSI, and explicit malware scanning are **not** implemented.
> A limited, optional yt-dlp-based media download path is available when the user installs external tools. See
> [docs/FEATURES.md](docs/FEATURES.md).

## Get a Windows installer using GitHub Actions

1. Create a **new empty repository** on GitHub (e.g. `fetch-download-manager`).
2. Extract this source ZIP **first**. Commit/upload its **contents**, including
   the hidden `.github/workflows/build.yml` folder, to the new repository's
   `main` branch. **Do not upload only the ZIP** — GitHub cannot build the
   files inside an unextracted archive.
3. Open the GitHub repository's **Actions** tab and select **Build Windows
   installer**. Choose **Run workflow** if it did not start automatically.
4. After the run succeeds, open the run's **Artifacts** area and download
   **Fetch-Windows-x64**. Extract that artifact ZIP to get:
   - `FetchSetup-win-x64.exe` — Windows setup installer (per-user, no admin rights)
   - `FetchPortable-win-x64.zip` — portable published application
5. Run `FetchSetup-win-x64.exe` on Windows. The installer creates a Start Menu
   shortcut. Windows may show an **unverified publisher** warning: the installer
   is **unsigned** until you arrange code signing yourself.

Optional: pushing a version tag like `v0.2.0` runs the workflow and, if the
repository permits it, attaches the binaries to a GitHub Release.

## Push from a Windows Terminal or macOS/Linux shell

From **inside the extracted `Fetch-GitHub` directory**:

```bash
git init
git add .
git commit -m "Initial Fetch source and Windows installer workflow"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/fetch-download-manager.git
git push -u origin main
```

Replace `YOUR_USERNAME` with your GitHub username. GitHub authentication must
be completed on your own machine. If you prefer the browser, you can use the
repository's **Add file → Upload files** feature after extracting the ZIP, but
ensure that the nested `.github/workflows/build.yml` file is committed (GitHub's
web uploader can be awkward for hidden folders).

## Build on a Windows development PC

Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Visual
Studio 2022 with .NET Desktop Development (optional), and
[Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```powershell
dotnet restore src/Fetch.Desktop/Fetch.Desktop.csproj
dotnet build src/Fetch.Desktop/Fetch.Desktop.csproj -c Release
dotnet run --project tests/Fetch.Tests/Fetch.Tests.csproj -c Release
dotnet publish src/Fetch.Desktop/Fetch.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer/Fetch.iss
```

Expected installer output: `artifacts/installer/FetchSetup-win-x64.exe`.

## How it works

- Saves files to your Downloads folder by default. Change the folder in the UI.
- Saves job metadata to `%LOCALAPPDATA%\Fetch\jobs.json` and part files under
  `%LOCALAPPDATA%\Fetch\parts`.
- Uses normal HTTPS certificate verification. Never overrides invalid TLS.
- If byte ranges are advertised and the file is big enough, opens up to 8
  concurrent transfers, verifies `Content-Range`, and joins pieces afterward.
- Resume is permitted only when the server supports it and provides a validator
  such as a strong ETag or Last-Modified. If the validator changes, the job
  discards stale segments rather than mixing two versions of the file.
- A partial file is retained if a job is removed from the queue; clearing
  `%LOCALAPPDATA%\Fetch\parts` manually reclaims that storage.
- Existing download files are never silently overwritten.

## Tests and constraints

`tests/Fetch.Tests` is a no-third-party-dependency console test runner.
It checks filename sanitization, segment math, state persistence, a local HTTP
range server, byte-perfect merging, and an ETag-change restart. No test downloads
anything from the public internet.

The source ZIP was prepared outside Windows, so its compiled `.exe` has **not**
been verified in this environment. GitHub Actions will actually compile and
run these tests on a Windows runner; if the job fails, inspect the build log.

MIT license. Original icon created for this project; no IDM branding or assets.


## Desktop upgrade in 0.2.0

- IDM-inspired **but independently branded** layout: toolbar, grouped categories,
  wider download table, progress/speed/remaining-time columns and details panel.
- Working actions: add direct URL, paste URL, resume selected, pause selected,
  pause all, resume unfinished, remove entry, clear completed history, open file,
  open its containing folder, change save directory and copy original URL.
- Automatic extension-based categories and clickable filters (All, Downloading,
  Unfinished, Finished, Video, Music, Compressed, Programs, Documents, Images, Other).
- ProgressBar explicitly binds read-only progress using OneWay mode to prevent
  the previous unhandled WPF binding exception.
- The current download engine and persisted `%LOCALAPPDATA%\Fetch` data format
  remain unchanged.

**Still not included:** automatic browser takeover, authenticated-site cookies,
HLS/DASH video capture, site grabber, queue time scheduler, proxy and bandwidth
controls, and the original C++/SQLite backend. This is not feature parity with IDM.
**Important:** Installer is unsigned, and the new source has not yet been
compiled on Windows in this authoring environment. GitHub Actions must pass
before trying the installer. Tests and build passing are not a full QA audit.


## v0.3: Fix unreadable selected row + Chrome/Edge extension

The WPF DataGrid now explicitly sets **dark teal selected-cell backgrounds and
white selected text**, including the inactive selection brushes. This fixes the
white-on-white highlighted download row visible in the 0.2 screenshot.

The browser companion supports **manual forwarding of direct HTTP(S) links**:
right-click a link and select **Download with Fetch**, or paste a direct link in
the extension popup. It uses **Chromium Manifest V3 + nativeMessaging**, then a
**current-user-only Windows named pipe** to the desktop application; no local
HTTP server is exposed. It forwards URLs only—never browsing history or cookies.

**Installation after the 0.3 Windows installer is built and installed:**

1. In Chrome, open `chrome://extensions` (Edge: `edge://extensions`).
2. Enable **Developer mode**, click **Load unpacked**, and select:
   `%LOCALAPPDATA%\Programs\Fetch\browser-extension`.
3. Copy the 32-character extension **ID** shown in the browser.
4. Open PowerShell and register the installed native host for that ID:

   ```powershell
   & "$env:LOCALAPPDATA\Programs\Fetch\scripts\Register-FetchBrowser.ps1" -ChromeId "PASTE_YOUR_CHROME_EXTENSION_ID"
   ```

   For Edge, use `-EdgeId "PASTE_EDGE_EXTENSION_ID"`. Both arguments can be
   supplied in the same command if you use both browsers.
5. Reload the extension. Right-click a **direct** `.zip`, `.exe`, `.pdf`, etc.
   link, select **Download with Fetch**, and confirm the job appears in Fetch.

**Automatic browser download capture** is included as an *experimental opt-in*
checkbox in the extension popup, **off by default**. When enabled, it attempts
to forward new HTTP(S) browser downloads to Fetch, canceling the browser copy
only after Fetch acknowledges it. Keep it off until manual forwarding works;
this feature has not been validated across sites and can be affected by
short-lived URLs, cookies, headers, and expiring download links.

**Not supported**: YouTube watch-page video extraction, DASH/HLS streaming,
protected streams, cookies from authenticated sessions, DRM, native Firefox
integration, reliable automatic capture of all browser download types, or
web-store publishing. Direct URL support alone cannot download videos from
ordinary streaming-site pages. The installer is **unsigned**.

The authoring environment is not Windows and cannot run the WPF/native-messaging
integration. GitHub Actions must compile 0.3, and the extension must be tested
manually on Windows before treating this release as reliable.

## Optional media downloader (0.4)

Fetch supports video URLs through a **user-installed** `yt-dlp.exe` and optional `ffmpeg.exe`.
Run `powershell -ExecutionPolicy Bypass -File scripts\Install-MediaTools.ps1` on Windows,
or download both tools yourself from the official authors. Restart Fetch after installation.
In Fetch, paste a URL, choose the desired quality, and click **Download video**.
Some media pages require logins, impose download restrictions, or use DRM; Fetch does not bypass these.
A successful build is not a guarantee that a provider URL will work.
