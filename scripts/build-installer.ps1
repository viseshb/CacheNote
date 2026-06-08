# Build StickyDeskSetup.exe: self-contained publish, then compile the Inno Setup script.
# Usage:  pwsh scripts\build-installer.ps1 [-Version 1.0.0]
param(
    [string]$Version = "1.0.0",
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\$Rid"
$app = Join-Path $root "StickyDesk.App\StickyDesk.App.csproj"

Write-Host "==> Publishing $Rid (self-contained) ..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $app -c Release -r $Rid --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Locate ISCC (Inno Setup 6).
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found." }

Write-Host "==> Compiling installer with $iscc ..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\StickyDesk.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "==> Done. Output in $(Join-Path $root 'dist')" -ForegroundColor Green
