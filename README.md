# Fetch 0.7 — Windows Download Manager

An independently branded Windows 10/11 x64 download manager built with .NET 8 and WPF. **Fetch is not Internet Download Manager and does not claim full IDM feature parity.** The long-term target is described in `docs/Original-full-specification.txt`.

## Included in this source version

- Direct HTTP(S) downloads with safe filenames, no silent overwrites, parallel ranged GETs, guarded byte offsets, status-aware retries, and disk-backed pause/resume.
- Resume checks a strong ETag or Last-Modified/size before using partial files. Completed files retain Windows Internet-zone marking. Windows antivirus may scan files, but **Fetch does not independently guarantee a malware scan**.
- Download categories, search-by-category filter, transfer rate and remaining time, pause/resume selected or all, completed history clearing, open file/folder, URL copying.
- Chrome/Edge **unpacked MV3 extension** for manual right-click download link forwarding through a per-user native messaging bridge. Experimental automatic capture is opt-in and off by default. A separate opt-in button appears when you hover an HTML5 video/audio element with a direct HTTP(S) source; it will not reveal DRM/blob or protected player-internal streams. The popup can also discover some already-loaded `.m3u8`/`.mpd` resource names on supported pages.
- Optional media URLs via user-installed **yt-dlp** and **FFmpeg**. Best/1080p/720p/480p/audio-only modes, where the source makes these available. No DRM bypass.
- **Since 0.5:** adjustable 1–32 connections per new direct download; adjustable 1–8 concurrent jobs (restart required); live-adjustable **global HTTP** speed limit; local-time scheduled starts while Fetch runs; optional expected SHA-256 checked **before the downloaded file is finalized**.
- **Since 0.6:** HTTP 404/403 fail without pointless retries; 408/425/429/500/502/503/504 receive bounded retries. Opt-in HTML5 media hover button.
- **New in 0.7:** right-click **Download page media with Fetch (yt-dlp)**; popup **Find exposed video/playlist URLs** from HTML5 sources, links, and already observed HLS/DASH resource names. Same-origin HTTP referrer is reduced to scheme + host + port without any path, query, or fragment. No browser login cookies, credentials, or Authorization headers are imported. Automatic capture is now opt-in **mirroring**, and never cancels the browser's download.
- JSON job and preference persistence under `%LOCALAPPDATA%\Fetch`; GitHub Actions workflow for a per-user Inno Setup EXE and portable Windows x64 ZIP.

## Push cumulative upgrade to your existing repository

The `Fetch-v0.7-Cumulative-Patch.zip` package is **cumulative** and can upgrade the earlier Fetch 0.2–0.5 repository. It includes `Apply-Upgrade.ps1` and `files/` holding the complete new project. It replaces the existing project source (but not your downloaded files or `%LOCALAPPDATA%\Fetch` data). It checks for a clean Git working tree and pushes to the current repository's main branch. Make sure the destination is your Fetch repository before running it.

```powershell
cd "E:\New folder (2)"
Expand-Archive -LiteralPath "$env:USERPROFILE\Downloads\Fetch-v0.7-Cumulative-Patch.zip" -DestinationPath ".\FetchV07Patch" -Force
powershell -NoProfile -ExecutionPolicy Bypass -File ".\FetchV07Patch\Apply-Upgrade.ps1" -RepoPath "E:\New folder (2)\Fetch-Download-Manager"
```

Then open `https://github.com/salmanshariarmy/Fetch-Download-Manager/actions`, wait for the new successful build, and download the `Fetch-Windows-x64` artifact. Extract it and install `installer/FetchSetup-win-x64.exe`. **The source was prepared outside Windows and has not been compiled here; an actual successful GitHub Actions run is required before installing.**

## Windows build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isinfo.php). From the repository root:

```powershell
dotnet restore src/Fetch.Desktop/Fetch.Desktop.csproj
dotnet build src/Fetch.Desktop/Fetch.Desktop.csproj -c Release
dotnet run --project tests/Fetch.Tests/Fetch.Tests.csproj -c Release
dotnet publish src/Fetch.Desktop/Fetch.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer/Fetch.iss
```

The setup EXE appears at `artifacts/installer/FetchSetup-win-x64.exe`; it is **unsigned**. Windows Defender and SmartScreen may warn on unfamiliar executables; investigate detections rather than bypassing them.

## Download options in the UI

Expand **Advanced options: speed, connections, checksum, schedule** beneath the URL entry.

- **Connections / job:** 1–32. Applies to newly queued direct HTTP(S) jobs; each server may offer fewer ranges. The number is fixed per job once started.
- **Concurrent jobs:** 1–8. Saves for next launch. Pending jobs wait their turn.
- **Global limit:** KiB/s; `0` removes the limit. Applies to all normal HTTP transfers sharing the running transfer engine, **not external yt-dlp subprocess traffic**. A short token bucket burst is allowed.
- **Expected SHA-256:** optional 64-character hex digest for your **next direct file** download. Wrong digest means failure without publishing a final filename; partial segments remain.
- **Scheduled start:** select an unfinished job, enter `yyyy-MM-dd HH:mm` in local time, press **Schedule start**. Keeps the item on disk and checks time approximately every 15 seconds. Fetch **must be running**. Clear schedule or manual Resume overrides the timing.

## Browser extension

1. Install Fetch's new EXE. In Chrome, open `chrome://extensions` (Edge: `edge://extensions`). Turn on Developer mode, choose **Load unpacked**, and select `%LOCALAPPDATA%\Programs\Fetch\browser-extension`.
2. Copy the browser's unpacked extension ID and run:

```powershell
& "$env:LOCALAPPDATA\Programs\Fetch\scripts\Register-FetchBrowser.ps1" -ChromeId "YOUR_ACTUAL_EXTENSION_ID"
```

Use `-EdgeId` for Edge. Reload the extension and right-click a direct file link to send it to Fetch. To enable the **HTML5 media button**, open the extension popup and check **Show Download with Fetch on direct HTML5 video/audio**; then reload the video tab and hover the video. The button only works when a direct HTTP(S) media URL is exposed by the element. MediaSource `blob:` URLs, encrypted players, site-specific API streams, and hidden/DRM streams are not supported.

The extension loads a lightweight content script on normal HTTP(S) pages but media controls are disabled until explicitly enabled. It does not read cookies or capture site-wide network requests. In the popup, **Mirror browser downloads to Fetch** is a separate opt-in mode. The browser copy is **never canceled**, even after Fetch queues the job, so it may create duplicate downloads. **Find exposed video/playlist URLs** scans HTML5 sources, manifest links, and resource URL names only when you press it. **Download current page media** sends the open page to yt-dlp; this can fail for unsupported or protected services. Browser-session authentication, real automatic takeover, and Firefox packaging are **not implemented**.

## Optional media support

You can install `yt-dlp.exe` and `ffmpeg.exe` yourself or use the optional helper:

```powershell
& "$env:LOCALAPPDATA\Programs\Fetch\scripts\Install-MediaTools.ps1"
```

Only download media you have permission to access. yt-dlp and FFmpeg must already be installed to process media jobs; direct HTTP(S) downloads need neither.

## Incomplete compared to IDM

This is **not** a faithful clone of IDM 6.43. It still lacks full page/video capture (beyond opt-in direct HTML5 sources), authenticated-browser cookie forwarding, FTP/FTPS, arbitrary proxy settings, site grabber, time-window scheduler with app autostart, tray controls, signature verification beyond optional user SHA-256, detailed connection diagnostics, a C++20/SQLite service architecture, a signed installer, and ARM64. Some capabilities require substantial independent engineering and security testing, not just copying the interface.

Project is MIT licensed with original branding. See `docs/FEATURES.md` for the actual status matrix.

## 0.7 tests and limitations

`tests/browser-extension/media-smoke.cjs` verifies direct media URL filtering, the opt-in button, and HLS/DASH discovery. `tests/browser-extension/bridge-smoke.cjs` covers explicit page media requests, stripped referrer origins, and non-destructive browser mirroring. `tests/Fetch.Tests/Program.cs` includes local HTTP tests for transient 503 retry and permanent 404 immediate failure. All tests use local resources; they do not fetch files from the internet. GitHub Actions builds the native host, runs tests, and packages the Inno Setup installer.

The source was prepared on Linux without a .NET compiler; the Windows build has not been verified in this environment. An actual green GitHub Actions run and Windows smoke test are mandatory before using the new installer.

## Browser features new in v0.7

- After installing Fetch v0.7, reload the unpacked Chrome/Edge extension. The native host uses the same registration ID as v0.6.
- Right-click a link to send a direct file, or right-click an ordinary webpage and select **Download page media with Fetch (yt-dlp)**. Media support depends on yt-dlp, the site, and your permissions.
- Open the popup and click **Find exposed video/playlist URLs** to find HTML5 media and HLS/DASH URL names. This cannot access all player streams.
- Opt-in **Mirror browser downloads** keeps the browser's original copy, unlike the previous experimental cancellation. This avoids losing a browser copy if Fetch later fails.
- Same-origin hotlink support sends only `scheme://host:port/` as an HTTP Referer, never a private page path or query. Cookies and Authorization are not imported.
