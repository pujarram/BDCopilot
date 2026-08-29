# Generates Tieto-branded Teams icons and BDCopilot-teams-app.zip
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

Add-Type -AssemblyName System.Drawing

function Save-Png {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [scriptblock]$Draw
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    & $Draw $g $Width $Height
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Tieto blue #021E57
$tietoNavy = [System.Drawing.Color]::FromArgb(255, 2, 30, 87)
$tietoAccent = [System.Drawing.Color]::FromArgb(255, 78, 96, 231) # #4E60E7
$white = [System.Drawing.Color]::White

$colorPath = Join-Path $here 'color.png'
Save-Png -Width 192 -Height 192 -Path $colorPath -Draw {
    param($g, $w, $h)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $tietoNavy), 0, 0, $w, $h)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $tietoAccent), 24, 24, $w - 48, $h - 48)
    $font = [System.Drawing.Font]::new('Segoe UI', 36, [System.Drawing.FontStyle]::Bold)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, $w, $h
    $g.DrawString('BD', $font, (New-Object System.Drawing.SolidBrush $white), $rect, $sf)
}

$outlinePath = Join-Path $here 'outline.png'
Save-Png -Width 32 -Height 32 -Path $outlinePath -Draw {
    param($g, $w, $h)
    $pen = New-Object System.Drawing.Pen $tietoNavy, 2
    $g.DrawEllipse($pen, 2, 2, $w - 5, $h - 5)
    $font = [System.Drawing.Font]::new('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, $w, $h
    $g.DrawString('BD', $font, (New-Object System.Drawing.SolidBrush $tietoNavy), $rect, $sf)
}

$pkg = Join-Path $here 'BDCopilot-teams-app'
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
Copy-Item (Join-Path $here 'manifest.json') (Join-Path $pkg 'manifest.json') -Force
Copy-Item $colorPath (Join-Path $pkg 'color.png') -Force
Copy-Item $outlinePath (Join-Path $pkg 'outline.png') -Force

$zipPath = Join-Path $here 'BDCopilot-teams-app.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zipPath

# Also mirror icons into Angular public/teams for local reference
$webTeams = Join-Path $here '..\..\bd-copilot-web\public\teams'
if (Test-Path $webTeams) {
    Copy-Item $colorPath (Join-Path $webTeams 'color.png') -Force
    Copy-Item $outlinePath (Join-Path $webTeams 'outline.png') -Force
}

Write-Host "Created: color.png, outline.png, BDCopilot-teams-app/, BDCopilot-teams-app.zip"
