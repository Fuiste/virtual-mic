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
& $dotnet restore tests/Fixtures/LegacyPlugin/LegacyPlugin.csproj --locked-mode
if ($LASTEXITCODE) { throw 'legacy fixture restore failed' }
& $dotnet build tests/Fixtures/LegacyPlugin/LegacyPlugin.csproj -c Release --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'legacy fixture build failed' }
New-Item -ItemType Directory -Force artifacts/compatibility-plugins/legacy | Out-Null
Copy-Item -LiteralPath tests/Fixtures/LegacyPlugin/bin/Release/net10.0/LegacyPlugin.dll,tests/Fixtures/LegacyPlugin/bin/Release/net10.0/LegacyPlugin.deps.json,tests/Fixtures/LegacyPlugin/plugin.json -Destination artifacts/compatibility-plugins/legacy
& $dotnet run --project tests/VirtualMic.Tests/VirtualMic.Tests.csproj -c Release --no-restore -p:UseSharedCompilation=false -- src/VirtualMic.App/bin/Release/net10.0-windows/plugins artifacts/compatibility-plugins
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
    $pluginRoot = (Resolve-Path -LiteralPath (Join-Path $OutputDirectory 'plugins')).Path
    $report = Join-Path $projectRoot 'artifacts/plugin-smoke.json'
    $appPath = (Resolve-Path -LiteralPath (Join-Path $OutputDirectory 'VirtualMic.exe')).Path
    $probe = Start-Process -FilePath $appPath -ArgumentList "--verify-plugins `"$pluginRoot`" `"$report`"" -WindowStyle Hidden -PassThru
    if (!$probe.WaitForExit(30000)) { Stop-Process -Id $probe.Id; throw 'plugin smoke timed out' }
    if ($probe.ExitCode -ne 0) { throw "packaged plugin smoke failed: $report" }
}
