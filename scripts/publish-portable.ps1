# Publishes the portable single-file win-x64 exe to ./publish/.
# Framework-dependent (.NET Desktop Runtime 9.0 required).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Stop-Process -Name "StudyCafe.Companion" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250

Write-Host "==> Publishing portable StudyCafe.Companion..." -ForegroundColor Cyan
dotnet publish "$root/src/StudyCafe.Companion/StudyCafe.Companion.csproj" `
    -c Release -r win-x64 --self-contained false `
    /p:PublishSingleFile=true `
    -o "$root/publish"

if ($LASTEXITCODE -ne 0) { 
    throw "dotnet publish failed with exit code $LASTEXITCODE (is an old instance still running and locking the exe? Run with --restart)" 
}

if (Test-Path "$root/assets/logo.ico") {
    Copy-Item "$root/assets/logo.ico" "$root/publish/logo.ico" -Force
}

Write-Host "==> Published successfully to $root/publish/StudyCafe.Companion.exe" -ForegroundColor Green
