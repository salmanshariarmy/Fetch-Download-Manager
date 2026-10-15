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
| Browser extensions, native messaging | Not included |
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
