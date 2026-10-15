# Fetch v0.7 feature status

This source is a functional incremental **C#/.NET 8 WPF** download manager, **not** a full IDM replacement. The original requirements file is an ambitious target, not a list of already delivered features.

| Feature | Implementation |
| --- | --- |
| Windows 10/11 x64 WPF app | Implemented; Windows build required |
| HTTP(S) downloads with TLS verification | Implemented |
| Multi-part HTTP transfers (1–32, server-dependent) | Implemented; configured for new jobs |
| Bounded retry, range integrity, ETag/Last-Modified resume | Implemented; v0.6 distinguishes transient 408/429/503 etc. from permanent 403/404; bounded Retry-After |
| Disk-backed history and paused downloads | Implemented via JSON files |
| Categories, filtering, queue pause/resume, completion history | Implemented |
| Global HTTP speed limit | New v0.5, token bucket; not applied to yt-dlp |
| Concurrent jobs 1–8 | New v0.5; settings apply on next launch |
| Local-time scheduled start | New v0.5; checks every 15s while app is open |
| Expected direct-file SHA-256 verification | New v0.5; fails before final rename |
| Chrome/Edge extension, right-click direct file links | Partial v0.3; manual native host registration |
| Automatic browser download interception | Safe opt-in mirroring, never cancels browser file; not full IDM capture |
| Media download using external yt-dlp and FFmpeg | Partial v0.4, separate user-installed tools |
| Firefox integration | Not implemented |
| HTML5 direct-media hover button | Opt-in v0.6; supports exposed HTTP(S) video/audio src only, no blob/DRM |
| Browser popup: find exposed HLS/DASH URLs | New v0.7; user-initiated DOM and resource name inspection; not arbitrary network sniffing |
| Browser popup: download current media page | New v0.7; sends page explicitly to yt-dlp, which must be installed |
| Browser same-origin referrer for hotlink support | New v0.7; preserves origin only, excludes paths/queries/cookies; not authenticated sessions |
| Deep network sniffing / video format preview | Limited to visible HTML5 sources, manifest URLs, and external yt-dlp; no DRM |
| Browser authentication/cookie session forwarding | Not implemented |
| Dedicated C++20 engine, SQLite, IPC CLI, WiX MSI | Not implemented; currently C# WPF and JSON |
| FTP/FTPS, manual proxies, credential managers | Not implemented |
| Website grabber, offline mirror | Not implemented |
| Time-window schedule, system autostart/service | Not implemented |
| Full per-host throttles, dynamic segment re-split | Not implemented |
| Windows toast/tray operation | Not implemented |
| Windows malware scanner integration / Authenticode signing | Not implemented |
| ARM64 installer | Not implemented |

## Security and stability

- No telemetry or browser cookie exfiltration is intentionally implemented. Browser native messaging accepts explicit URL/media forwarding and same-origin referrer origin only, and the extension is opt-in.
- Windows system TLS trust is used for HTTPS. Unsafe server filenames are sanitized. Existing destination files are not silently replaced.
- Optional user-supplied SHA-256 is verified before the rename, and failed parts remain on disk. It does not substitute for malware scanning.
- The browser extension, media integration, and new Windows desktop changes still require end-to-end QA on actual Windows installations.

## Testing

CI builds the desktop and native host, executes core unit/local HTTP integration tests and JavaScript browser bridge and media UI smoke tests, publishes both applications, and compiles Inno Setup. Passing CI is necessary, not proof of complete feature parity.
