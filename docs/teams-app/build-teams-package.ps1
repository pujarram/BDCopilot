# Builds a flat-root BDCopilot-teams-app.zip for Teams sideload / admin upload.
# Run from repo root or docs/teams-app:  .\docs\teams-app\build-teams-package.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

# Regenerate icons first (also updates bd-copilot-web/public/teams icons).
& (Join-Path $here 'generate-icons.ps1')

$manifestPath = Join-Path $here 'manifest.json'
$colorPath = Join-Path $here 'color.png'
$outlinePath = Join-Path $here 'outline.png'
$zipPath = Join-Path $here 'BDCopilot-teams-app.zip'

foreach ($f in @($manifestPath, $colorPath, $outlinePath)) {
    if (-not (Test-Path $f)) { throw "Missing required file: $f" }
}

# Validate manifest JSON + bot wiring before zipping.
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if (-not $manifest.bots -or $manifest.bots.Count -lt 1) { throw 'manifest.json must include a bots[] entry.' }
$botId = $manifest.bots[0].botId
if ([string]::IsNullOrWhiteSpace($botId)) { throw 'bots[0].botId is empty.' }
if ($manifest.webApplicationInfo.id -eq $botId) {
    throw 'Invalid manifest: webApplicationInfo.id must be the Graph/SSO app — NOT the botId. See README.'
}
if ($botId -eq '96c378fc-3d41-452c-85cb-f9f742f9c037') {
    throw 'Invalid manifest: botId is the Graph app id. Use Azure Bot (BDAgent) id 2088516d-bf3b-40d6-88b5-6acc44929b6d.'
}

Write-Host "Manifest id (Teams app package): $($manifest.id)"
Write-Host "botId (Azure Bot Microsoft App ID): $botId"
Write-Host "SSO app (webApplicationInfo.id): $($manifest.webApplicationInfo.id)"

# Mirror into package folder for inspection.
$pkg = Join-Path $here 'BDCopilot-teams-app'
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
Copy-Item $manifestPath (Join-Path $pkg 'manifest.json') -Force
Copy-Item $colorPath (Join-Path $pkg 'color.png') -Force
Copy-Item $outlinePath (Join-Path $pkg 'outline.png') -Force

# Flat zip — entries MUST be at root (manifest.json, not BDCopilot-teams-app/manifest.json).
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('manifest.json', 'color.png', 'outline.png')) {
        $src = Join-Path $here $name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $name) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

# Verify zip structure.
$verify = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($verify.Entries | ForEach-Object { $_.FullName })
    Write-Host 'Zip entries:' ($entries -join ', ')
    if ($entries -notcontains 'manifest.json') { throw 'Zip missing manifest.json at root.' }
    if ($entries | Where-Object { $_ -match '/' -or $_ -match '\\' }) {
        throw 'Zip contains nested folders — Teams will reject or misread the package.'
    }
}
finally {
    $verify.Dispose()
}

# Sync Angular reference manifest (same URLs / ids).
$webManifest = Join-Path $here '..\..\bd-copilot-web\public\teams\manifest.json'
if (Test-Path $webManifest) {
    Copy-Item $manifestPath $webManifest -Force
}

Write-Host ''
Write-Host "OK: $zipPath ($((Get-Item $zipPath).Length) bytes)"
Write-Host 'Upload this zip in Teams (Manage your apps -> Upload a custom app).'
Write-Host ''
Write-Host 'If you still see Invalid Bot, fix Azure (not the zip):'
Write-Host '  1. Azure Portal -> Azure Bot (BDAgent) -> Configuration -> Microsoft App ID = $botId'
Write-Host '  2. Azure Bot -> Channels -> Microsoft Teams -> Enabled'
Write-Host '  3. Messaging endpoint -> https://<api-host>/api/channels/teams/messages'
