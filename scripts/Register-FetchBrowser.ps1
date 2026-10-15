param(
    [string]$ChromeId = '',
    [string]$EdgeId = '',
    [string]$InstallDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $InstallDir) {
    $InstallDir = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}
$hostExe = Join-Path $InstallDir 'native\Fetch.NativeHost.exe'
if (-not (Test-Path -LiteralPath $hostExe)) {
    throw "Native host not found at $hostExe. Install the new Fetch installer first."
}
$manifestPath = Join-Path $InstallDir 'native\com.fetch.download_manager.json'
$existing = @()
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($origin in $previous.allowed_origins) {
            if ($origin -match '^chrome-extension://([a-p]{32})/$') { $existing += $Matches[1] }
        }
    }
    catch { Write-Warning 'Existing native messaging manifest was unreadable; creating a fresh one.' }
}
$ids = @($ChromeId, $EdgeId) + $existing
$ids = @($ids | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
if (-not $ChromeId -and -not $EdgeId) {
    throw 'Supply -ChromeId and/or -EdgeId from chrome://extensions or edge://extensions (Developer mode).'
}
foreach ($id in $ids) {
    if ($id -notmatch '^[a-p]{32}$') { throw "Invalid Chromium extension ID: $id" }
}
$manifest = @{
    name = 'com.fetch.download_manager'
    description = 'Local Fetch Download Manager native messaging bridge'
    path = $hostExe
    type = 'stdio'
    allowed_origins = @($ids | ForEach-Object { "chrome-extension://$_/" })
}
$json = ConvertTo-Json -InputObject $manifest -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $json, [System.Text.UTF8Encoding]::new($false))
if ($ChromeId) {
    $key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.fetch.download_manager'
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $manifestPath
    Write-Host 'Chrome native messaging registered for this Windows user.' -ForegroundColor Green
}
if ($EdgeId) {
    $key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.fetch.download_manager'
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $manifestPath
    Write-Host 'Edge native messaging registered for this Windows user.' -ForegroundColor Green
}
Write-Host "Host manifest: $manifestPath"
Write-Host 'Reload the browser extension, then right-click a direct download link > Download with Fetch.'
Write-Host 'This does not install or publish the extension to Chrome Web Store / Edge Add-ons.'
