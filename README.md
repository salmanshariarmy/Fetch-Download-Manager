# Fetch — Windows Download Manager (0.1.0 MVP)

A source-code download manager for **Windows 10/11 x64**. Paste a direct HTTP(S)
file URL, start the download, pause or resume it, and keep your download history
across app restarts. Ranged downloads use up to eight connections.

> **Scope:** This is an implemented starter application, **not** the complete
> IDM-scale application in `docs/Original-full-specification.txt`. In particular,
> the C++/SQLite engine, browser integration, media pipeline, site grabber,
> signed MSI, and explicit malware scanning are **not** implemented. See
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

Optional: pushing a version tag like `v0.1.0` runs the workflow and, if the
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
