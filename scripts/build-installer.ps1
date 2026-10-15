<#
.SYNOPSIS
    Builds the Study Cafe Companion application and compiles the Inno Setup installer.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> Publishing portable single-file StudyCafe.Companion..." -ForegroundColor Cyan
& "$PSScriptRoot\publish-portable.ps1"

Write-Host "==> Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Cyan
$cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
$iscc = if ($cmd) { $cmd.Source } else { $null }

if (-not $iscc) {
    $searchPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $searchPaths) {
        if (Test-Path $path) {
            $iscc = $path
            break
        }
    }
}

if (-not $iscc) {
    if ($env:CI -eq "true") {
        throw "ISCC.exe was not found in the CI runner environment."
    }
    Write-Warning "ISCC.exe not found on local machine. Portable exe is ready in .\publish\. In GitHub Actions, Inno Setup is installed automatically to generate the setup exe."
    exit 0
}

Write-Host "==> Using Inno Setup compiler: $iscc" -ForegroundColor Green
$issFile = Join-Path $root "installer\Installer-x64.iss"

Write-Host "==> Compiling installer: $issFile" -ForegroundColor Cyan
& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    throw "ISCC compilation failed with exit code $LASTEXITCODE"
}

$outputExe = Join-Path $root "installer\output\StudyCafeCompanion-Setup-x64.exe"
Write-Host "==> Installer successfully built: $outputExe" -ForegroundColor Green
