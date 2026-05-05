# Publica SmoothMice e compila o instalador Inno Setup 6.
# Requer: .NET SDK (qualquer versão que suporte net48) + Inno Setup 6 (ISCC.exe)
#
# Targeting .NET Framework 4.8 — pré-instalado no Windows 10/11.
# Sem runtime bundling: instalador ~2-4 MB (vs 64 MB self-contained).

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Error "SDK .NET nao encontrado no PATH. Instala .NET SDK: https://dotnet.microsoft.com/download"
}

Write-Host ">> dotnet publish... (Target: net48, sem runtime bundling)"

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
