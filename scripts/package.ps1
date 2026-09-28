param(
    [string]$BuildDirectory = 'artifacts/virtual-mic-win-x64',
    [string]$OutputDirectory = 'artifacts/releases'
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
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$exeHash = (Get-FileHash -LiteralPath (Join-Path $BuildDirectory 'VirtualMic.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
@("$zipHash  $zipName", "$exeHash  VirtualMic.exe") |
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Get-Item -LiteralPath $zipPath | Select-Object Name, Length
Get-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt')
