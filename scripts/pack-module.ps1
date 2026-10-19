<#
    Builds the SwissTechKnife module package:
      artifacts/module/smooth-mice-<ver>.zip
        module.json, SmoothMice.Module.dll        (loaded by the host)
        app/SmoothMice.Hosted.exe (+ dlls)        (SmoothMice.App, run in its own process with --hosted)
    The SDK dll is never packaged: the host provides it. Prints sha256 and size for the host catalog.
    powershell -File scripts/pack-module.ps1
#>
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$moduleProject = Join-Path $root 'src/SmoothMice.Module'
$appProject = Join-Path $root 'src/SmoothMice.App'
$manifest = Get-Content (Join-Path $moduleProject 'module.json') -Raw | ConvertFrom-Json

foreach ($project in $moduleProject, $appProject) {
    dotnet build $project -c $Configuration -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

$moduleOut = Join-Path $moduleProject "bin/$Configuration/net48"
$appOut = Join-Path $appProject "bin/$Configuration/net48"
$appExe = Get-ChildItem $appOut -Filter 'SmoothMice-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $appExe) { throw "SmoothMice-*.exe not found in $appOut" }

$outDir = Join-Path $root 'artifacts/module'
New-Item -ItemType Directory -Force $outDir | Out-Null
$zipPath = Join-Path $outDir "$($manifest.id)-$($manifest.version).zip"
if (Test-Path $zipPath) { Remove-Item $zipPath }

$skip = '.pdb', '.xml'
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    function Add-Entry([string]$file, [string]$entry) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $entry, 'Optimal') | Out-Null
    }
    Get-ChildItem $moduleOut -File |
        Where-Object { $_.Name -notlike 'SwissTechKnife.Sdk.*' -and $_.Extension -notin $skip } |
        ForEach-Object { Add-Entry $_.FullName $_.Name }
    Get-ChildItem $appOut -File |
        Where-Object { $_.Extension -notin $skip -and $_.Name -notlike 'SwissTechKnife.Sdk.*' } |
        ForEach-Object {
            # The hosted copy gets its own image name: the standalone OTA batch kills SmoothMice.exe by name.
            $name = $_.Name.Replace($appExe.Name, 'SmoothMice.Hosted.exe')
            Add-Entry $_.FullName "app/$name"
        }
} finally { $zip.Dispose() }

$hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $zipPath).Length
Write-Host "Packed $zipPath" -ForegroundColor Green
Write-Host "  version $($manifest.version)"
Write-Host "  sha256  $hash"
Write-Host "  size    $size"
[pscustomobject]@{ Id = $manifest.id; Version = $manifest.version; File = $zipPath; Sha256 = $hash; Size = $size }
