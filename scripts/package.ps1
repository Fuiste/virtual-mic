param(
    [string]$BuildDirectory = 'artifacts/virtual-mic-win-x64',
    [string]$OutputDirectory = 'artifacts/releases',
    [string]$DelayDirectory = 'artifacts/sample-plugins/delay'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
[xml]$project = Get-Content -LiteralPath src/VirtualMic.App/VirtualMic.App.csproj -Raw
$version = $project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[a-z0-9.]+)?$') { throw 'invalid release version' }
$zipName = "virtual-mic-v$version-win-x64.zip"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zipPath = Join-Path $OutputDirectory $zipName
# Explicit distribution allowlist: no debug symbols, device inventory, user library,
# local SDK, driver installer, logs, or development fixtures enter the package.
$files = @('VirtualMic.exe', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'docs', 'licenses', 'plugin-sdk') |
    ForEach-Object { Join-Path $BuildDirectory $_ }
foreach ($file in $files) { if (!(Test-Path -LiteralPath $file)) { throw "missing package input: $file" } }
Compress-Archive -LiteralPath $files -DestinationPath $zipPath -CompressionLevel Optimal -Force
# Package the reference separately, with an installable delay/ root and an explicit
# allowlist. Do not include debug symbols, build caches, or host assemblies.
$delayZipName = "virtual-mic-delay-v$version.zip"
$delayZipPath = Join-Path $OutputDirectory $delayZipName
$delayFiles = @('VirtualMic.Delay.dll', 'VirtualMic.Delay.deps.json', 'plugin.json', 'README.md', 'LICENSE') |
    ForEach-Object { Join-Path $DelayDirectory $_ }
foreach ($file in $delayFiles) { if (!(Test-Path -LiteralPath $file)) { throw "missing delay package input: $file" } }
$delayStream = [System.IO.File]::Open($delayZipPath, [System.IO.FileMode]::Create)
try {
    $delayArchive = [System.IO.Compression.ZipArchive]::new($delayStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $delayFiles) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($delayArchive, $file, ('delay/' + (Split-Path $file -Leaf))) | Out-Null
        }
    } finally { $delayArchive.Dispose() }
} finally { $delayStream.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$exeHash = (Get-FileHash -LiteralPath (Join-Path $BuildDirectory 'VirtualMic.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$delayZipHash = (Get-FileHash -LiteralPath $delayZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$delayDllHash = (Get-FileHash -LiteralPath (Join-Path $DelayDirectory 'VirtualMic.Delay.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
@("$zipHash  $zipName", "$exeHash  VirtualMic.exe", "$delayZipHash  $delayZipName", "$delayDllHash  delay/VirtualMic.Delay.dll") |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Get-Item -LiteralPath $zipPath,$delayZipPath | Select-Object Name, Length
Get-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt')
