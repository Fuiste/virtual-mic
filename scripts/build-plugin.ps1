param([string]$OutputDirectory = 'artifacts/sample-plugins/echo')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$taskTemp = Join-Path $projectRoot '.tools/tmp'
New-Item -ItemType Directory -Force -Path $taskTemp | Out-Null
$env:TEMP = $taskTemp
$env:TMP = $taskTemp
$localDotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
& $dotnet restore examples/VirtualMic.Echo/VirtualMic.Echo.csproj --locked-mode
if ($LASTEXITCODE) { throw 'plugin restore failed' }
& $dotnet build examples/VirtualMic.Echo/VirtualMic.Echo.csproj -c Release --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'plugin build failed' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -Path examples/VirtualMic.Echo/bin/Release/net10.0/VirtualMic.Echo.* -Destination $OutputDirectory -Force
Copy-Item -LiteralPath examples/VirtualMic.Echo/plugin.json -Destination $OutputDirectory -Force
Write-Output "built echo plugin in $OutputDirectory (not installed)"
