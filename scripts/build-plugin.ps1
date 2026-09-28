param([string]$OutputDirectory = 'artifacts/sample-plugins/delay')
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
& $dotnet restore examples/VirtualMic.Delay/VirtualMic.Delay.csproj --locked-mode
if ($LASTEXITCODE) { throw 'plugin restore failed' }
& $dotnet build examples/VirtualMic.Delay/VirtualMic.Delay.csproj -c Release --no-restore -p:UseSharedCompilation=false
if ($LASTEXITCODE) { throw 'plugin build failed' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -Path examples/VirtualMic.Delay/bin/Release/net10.0/VirtualMic.Delay.* -Destination $OutputDirectory -Force
Copy-Item -LiteralPath examples/VirtualMic.Delay/plugin.json,examples/VirtualMic.Delay/README.md -Destination $OutputDirectory -Force
Copy-Item -LiteralPath LICENSE -Destination $OutputDirectory -Force
Write-Output "built delay plugin in $OutputDirectory (not installed)"
