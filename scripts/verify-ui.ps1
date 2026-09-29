param([string]$AppDirectory = 'artifacts/virtual-mic-win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:TEMP = Join-Path $projectRoot '.tools\tmp'
$env:TMP = $env:TEMP
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:TEMP 'bundles'
New-Item -ItemType Directory -Force -Path $env:TEMP, artifacts, artifacts\library-check | Out-Null
$executable = Join-Path (Join-Path $projectRoot $AppDirectory) 'VirtualMic.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'run scripts/build.ps1 -Publish first' }
foreach ($view in 'preview', 'cleanup', 'compact', 'empty', 'error') {
    $imagePath = Join-Path $projectRoot "artifacts\ui-$view.png"
    $process = Start-Process -FilePath $executable -ArgumentList @("--render-$view", "`"$imagePath`"") -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "ui $view failed; see $imagePath.error.txt" }
    Get-Content -LiteralPath "$imagePath.json"
}
$libraryPath = Join-Path $projectRoot 'artifacts\library-check'
$process = Start-Process -FilePath $executable -ArgumentList @('--verify-library', "`"$libraryPath`"") -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'library smoke test failed; see artifacts/library-check/error.txt' }
Get-Content -LiteralPath (Join-Path $libraryPath 'result.json')
