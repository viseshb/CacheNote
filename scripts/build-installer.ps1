# Build CacheNoteSetup.exe: self-contained publish, then compile the Inno Setup script.
# Usage:  pwsh scripts\build-installer.ps1 [-Version 1.2.3]
param(
    [string]$Version = "",
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"

# No hardcoded default: a stale "1.0.0" built a DOWNGRADING installer (Inno has no version
# gate on the same AppId) and the updater then nagged forever. Derive from the latest v* tag.
if (-not $Version) {
    $tag = git describe --tags --match "v*" --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $Version = $tag.Trim().TrimStart("v")
        Write-Host "==> No -Version given; using latest git tag: $Version" -ForegroundColor Yellow
    } else {
        throw "No -Version given and no v* git tag found. Pass -Version explicitly."
    }
}
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "publish\$Rid"
$app = Join-Path $root "CacheNote.App\CacheNote.App.csproj"

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
& $iscc "/DMyAppVersion=$Version" (Join-Path $root "installer\CacheNote.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "==> Done. Output in $(Join-Path $root 'dist')" -ForegroundColor Green
