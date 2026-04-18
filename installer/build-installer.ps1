# Publica SmoothMice (win-x64) e compila o instalador Inno Setup 6.
# Requer: .NET 8 SDK + Inno Setup 6 (ISCC.exe)
#
# Por defeito: self-contained + single-file (~64 MB setup) — instalador completo, sem runtime .NET no PC alvo.
# Instalador leve (framework-dependent ~0.2 MB exe no payload; runtime .NET obrigatório no alvo):
#   powershell -File installer\build-installer.ps1 -FrameworkDependent
# -SelfContained mantem-se como no-op util (compat); o padrao ja e autocontido.

param(
  [switch]$FrameworkDependent,
  [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error "SDK .NET nao encontrado no PATH. Instala .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

$useSelfContained = -not $FrameworkDependent
Write-Host ">> dotnet publish... (SelfContained=$useSelfContained; FrameworkDependent=$FrameworkDependent)"

if ($useSelfContained) {
  dotnet publish "src/SmoothMice.App/SmoothMice.App.csproj" `
    -c Release `
    -r win-x64 `
    -p:SelfContained=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:PublishDebugSymbols=false
}
else {
  dotnet publish "src/SmoothMice.App/SmoothMice.App.csproj" `
    -c Release `
    -r win-x64 `
    -p:SelfContained=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
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

$appCsproj = Join-Path $repoRoot "src\SmoothMice.App\SmoothMice.App.csproj"
$appVersion = dotnet msbuild $appCsproj -getProperty:Version -nologo
if ([string]::IsNullOrWhiteSpace($appVersion)) {
  Write-Error "Nao foi possivel ler <Version> do projeto (Directory.Build.props)."
}

$assemblyName = dotnet msbuild $appCsproj -getProperty:AssemblyName -nologo
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
  Write-Error "Nao foi possivel ler AssemblyName do projeto."
}
$publishedExe = "$assemblyName.exe"

Write-Host ">> ISCC (Inno)... (MyAppVersion=$appVersion; MyPublishedExe=$publishedExe)"
& $iscc "/DMyAppVersion=$appVersion" "/DMyPublishedExe=$publishedExe" (Join-Path $PSScriptRoot "SmoothMice.Installer.iss")

$out = Join-Path $repoRoot "artifacts\installer"
Write-Host ""
Write-Host "Instalador gerado em: $out"
