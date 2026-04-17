# Publica SmoothMice (win-x64) e compila o instalador Inno Setup 6.
# Requer: .NET 8 SDK + Inno Setup 6 (ISCC.exe)
#
# Por defeito: framework-dependent + single-file (~230 KB exe + pdbs).
#   O PC alvo precisa de ".NET 8 Desktop Runtime" (Windows x64).
# Para bundle com runtime .NET (~70 MB exe autocontido):
#   powershell -File installer\build-installer.ps1 -SelfContained

param(
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error "SDK .NET nao encontrado no PATH. Instala .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

Write-Host ">> dotnet publish... (SelfContained=$SelfContained)"

if ($SelfContained) {
  dotnet publish "src/SmoothMice.App/SmoothMice.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishDebugSymbols=false
}
else {
  dotnet publish "src/SmoothMice.App/SmoothMice.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishDebugSymbols=false
}

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
Inno Setup 6 nao encontrado.
Instala de: https://jrsoftware.org/isdl.php
Depois volta a correr este script.
"@
}

Write-Host ">> ISCC (Inno)..."
& $iscc (Join-Path $PSScriptRoot "SmoothMice.Installer.iss")

$out = Join-Path $repoRoot "artifacts\installer"
Write-Host ""
Write-Host "Instalador gerado em: $out"
