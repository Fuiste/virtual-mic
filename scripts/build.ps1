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
& "$PSScriptRoot/build-plugin.ps1"
& $dotnet run --project tests/VirtualMic.Tests/VirtualMic.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -- artifacts/sample-plugins
if ($LASTEXITCODE) { throw 'audio tests failed' }
if ($Publish) {
    & $dotnet publish src/VirtualMic.App/VirtualMic.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetLockFilePath=packages.publish.lock.json -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o $OutputDirectory
    if ($LASTEXITCODE) { throw 'publish failed' }
    Copy-Item -LiteralPath README.md, LICENSE, THIRD-PARTY-NOTICES.md -Destination $OutputDirectory
    Copy-Item -LiteralPath licenses -Destination $OutputDirectory -Recurse -Force
    Copy-Item -LiteralPath docs -Destination $OutputDirectory -Recurse -Force
    $pluginSdk = Join-Path $OutputDirectory 'plugin-sdk'
    New-Item -ItemType Directory -Force -Path $pluginSdk | Out-Null
    Copy-Item -LiteralPath src/VirtualMic.PluginApi/bin/Release/net10.0/VirtualMic.PluginApi.dll -Destination $pluginSdk
    $pluginRoot = (Resolve-Path -LiteralPath artifacts/sample-plugins).Path
    $report = Join-Path $projectRoot 'artifacts/plugin-smoke.json'
    $appPath = (Resolve-Path -LiteralPath (Join-Path $OutputDirectory 'VirtualMic.exe')).Path
    $probe = Start-Process -FilePath $appPath -ArgumentList "--verify-plugins `"$pluginRoot`" `"$report`"" -WindowStyle Hidden -PassThru
    if (!$probe.WaitForExit(30000)) { Stop-Process -Id $probe.Id; throw 'plugin smoke timed out' }
    if ($probe.ExitCode -ne 0) { throw "packaged plugin smoke failed: $report" }
}
