# Scope of this repository

The uploaded `Original-full-specification.txt` requests an entire IDM-scale product with
C++20 engine, WinHTTP/WinINet, SQLite, WPF, browser add-ons, media download, grabber,
native messaging, MSI and code signing. The current repository is an independently
implemented **.NET 8 WPF functional MVP**, NOT a full implementation of that spec.

| Feature | Status |
| --- | --- |
| Windows desktop app with URL entry, queue, categories, toolbar and filters | Implemented (0.2 source) |
| HTTP(S), system TLS verification and normal redirects | Implemented |
| Parallel HTTP byte-range transfers (up to 8) | Implemented |
| Pause, resume from disk part files | Implemented when server has strong ETag or Last-Modified and supports ranges |
| Automatic restart when ETag/size changes | Implemented |
| Persistence across application restart | Implemented (JSON store) |
| Safe names, no overwrite, Windows Internet Zone marking | Implemented |
| Global maximum of 3 active jobs | Implemented |
| Retry transient connection failures | Implemented (bounded exponential backoff) |
| GitHub Actions builds x64 setup EXE and portable ZIP | Workflow included; needs execution on Windows runner |
| Local HTTP integration tests | Included; not executed in this Linux authoring environment |
| C++20 engine, CLI, named pipe, SQLite, WiX MSI | Not included |
| Chrome/Edge companion, native messaging | Partial v0.3: manual direct links, optional experimental download capture |
| HLS/DASH/yt-dlp/ffmpeg, site grabber, FTP/FTPS | Not included |
| Category filters and automatic extension classification | Implemented (0.2 source) |
| Scheduler, authentication, proxies, throttle | Not included |
| Explicit malware scanning via attachment services | Not included (Internet Zone is marked, NOT scanned by this app) |
| Authenticode code signing | Not included (requires your own code-signing certificate) |
| ARM64 installer | Not included |

## Security boundaries

No telemetry, no browser history access, no cookie collection. The software reads only
the HTTP(S) URL given by the user and stores job details locally under
`%LOCALAPPDATA%\\Fetch`. It relies on Windows TLS trust settings and the operating
system's installed security protections. A direct HTTP URL is not encrypted.

## Known limitations

- Some servers ignore ranges or change content unexpectedly; Fetch falls back to one
  stream if the initial server range claims are false, but will report failure for
  unsafe mid-download changes. Partial data is retained for troubleshooting.
- Authenticated sites requiring browser session cookies are not supported.
- Multi-file concurrent naming has a small race if two server requests finish at
  precisely the same time with the same generated filename; a collision fails safely
  instead of overwriting the first file.
- A portable ZIP is separate from the setup installer.

## v0.3 browser companion

- Fixed dark selected DataGrid row/cell colors (no white-on-white selection).
- Added Manifest V3 Chrome/Edge unpacked browser extension, popup and right-click
  **Download with Fetch** on direct HTTP(S) links.
- Added per-user Windows native messaging host and named pipe to queue URLs in
  the desktop application. No local network listener or cookie access.
- Added optional *experimental* automatic browser download capture, default off.
- Registration is manual using `scripts/Register-FetchBrowser.ps1` and the
  actual Chrome/Edge unpacked extension ID.
- Still missing authenticated browser session downloading, site grabber,
  streaming video integration and complete IDM feature parity.

## Fetch 0.4: opt-in media download

- Choose **Download video** for an HLS/DASH playlist, a supported video page, or another URL supported by yt-dlp.
- YouTube/Vimeo watch URLs and `.m3u8`/`.mpd` links entered using **Add URL** route through yt-dlp automatically.
- Choose Best, 1080p, 720p, 480p, or Audio only. Availability depends on the remote site.
- yt-dlp is **not bundled**: install it separately using scripts/Install-MediaTools.ps1 or official downloads. FFmpeg improves quality/muxing.
- Browser extension right-click links can send video-page URLs via the existing bridge; video detection overlays are not yet supported.
- No bypass of DRM, paywalls, access controls, or site restrictions. No downloaded cookies are forwarded.
- Media resume is delegated to yt-dlp and may vary by provider. Not a full IDM-compatible media implementation.
