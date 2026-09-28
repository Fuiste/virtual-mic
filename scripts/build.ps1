param([switch]$Publish, [string]$OutputDirectory = 'artifacts/virtual-mic-win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$taskTemp = Join-Path $projectRoot '.tools\tmp'
New-Item -ItemType Directory -Force -Path $taskTemp | Out-Null
$env:TEMP = $taskTemp
$env:TMP = $taskTemp
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
& $dotnet restore src/VirtualMic.App/VirtualMic.App.csproj --locked-mode
if ($LASTEXITCODE) { throw 'app restore failed' }
& $dotnet build src/VirtualMic.App/VirtualMic.App.csproj -c Release --no-restore -p:UseSharedCompilation=false -m:1
if ($LASTEXITCODE) { throw 'app build failed' }
& $dotnet restore tests/VirtualMic.Tests/VirtualMic.Tests.csproj --locked-mode
if ($LASTEXITCODE) { throw 'test restore failed' }
& $dotnet run --project tests/VirtualMic.Tests/VirtualMic.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'audio tests failed' }
if ($Publish) {
    & $dotnet publish src/VirtualMic.App/VirtualMic.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetLockFilePath=packages.publish.lock.json -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o $OutputDirectory
    if ($LASTEXITCODE) { throw 'publish failed' }
    Copy-Item -LiteralPath README.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $OutputDirectory
    Copy-Item -LiteralPath licenses -Destination $OutputDirectory -Recurse -Force
    Copy-Item -LiteralPath docs -Destination $OutputDirectory -Recurse -Force
}
