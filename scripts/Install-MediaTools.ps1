# Optional dependencies. This script does not disable Windows Defender or
# install a browser extension. Review official licensing/terms before use.
$ErrorActionPreference = 'Stop'
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget is not installed. Install yt-dlp and FFmpeg from their official download pages instead.'
}
Write-Host 'Fetch media download uses two optional third-party tools.'
Write-Host 'yt-dlp: https://github.com/yt-dlp/yt-dlp'
Write-Host 'FFmpeg:  https://ffmpeg.org/'
$answer = Read-Host 'Install yt-dlp and FFmpeg using winget? (y/N)'
if ($answer -ne 'y' -and $answer -ne 'Y') {
    Write-Host 'Cancelled. No packages installed.'
    return
}
& winget install --id yt-dlp.yt-dlp --exact --accept-source-agreements --accept-package-agreements
if ($LASTEXITCODE -ne 0) { throw 'yt-dlp installation failed. Check winget logs.' }
& winget install --id Gyan.FFmpeg --exact --accept-source-agreements --accept-package-agreements
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg installation failed. Check winget logs.' }
Write-Host 'Restart Fetch and PowerShell so PATH changes take effect.' -ForegroundColor Green
