# Publishes SmoothMice and compiles the Inno Setup 6 installer.
# Requires: .NET SDK (any version that supports net48) + Inno Setup 6 (ISCC.exe)
#
# Targets .NET Framework 4.8 — preinstalled on Windows 10/11.
# No runtime bundling: installer ~2-4 MB (vs 64 MB self-contained).

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error ".NET SDK not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
}

Write-Host ">> dotnet publish... (Target: net48, no runtime bundling)"

dotnet publish "src/SmoothMice.App/SmoothMice.App.csproj" `
  -c Release `
  -p:PublishDebugSymbols=false

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

$iscc = @(
  "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  Write-Error @"
Inno Setup 6 not found.
Install it from: https://jrsoftware.org/isdl.php
Then run this script again.
"@
}

$appCsproj = Join-Path $repoRoot "src\SmoothMice.App\SmoothMice.App.csproj"
$appVersion = dotnet msbuild $appCsproj -getProperty:Version -nologo
if ([string]::IsNullOrWhiteSpace($appVersion)) {
  Write-Error "Could not read <Version> from the project (Directory.Build.props)."
}

$assemblyName = dotnet msbuild $appCsproj -getProperty:AssemblyName -nologo
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
  Write-Error "Could not read AssemblyName from the project."
}
$publishedExe = "$assemblyName.exe"

Write-Host ">> ISCC (Inno)... (MyAppVersion=$appVersion; MyPublishedExe=$publishedExe)"
& $iscc "/DMyAppVersion=$appVersion" "/DMyPublishedExe=$publishedExe" (Join-Path $PSScriptRoot "SmoothMice.Installer.iss")

$out = Join-Path $repoRoot "artifacts\installer"
Write-Host ""
Write-Host "Installer written to: $out"
