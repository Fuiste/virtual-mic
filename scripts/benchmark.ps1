param([string]$BaselineRef = 'v0.3.0-preview.1', [string]$OutputDirectory = 'artifacts/performance-comparison')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$env:TEMP = Join-Path $projectRoot '.tools\tmp'
$env:TMP = $env:TEMP
$dotnet = if (Test-Path -LiteralPath .tools/dotnet/dotnet.exe) { Join-Path $projectRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
# A read-only Git source export isolates the released core/plugins without moving
# the current checkout or editing the baseline. Both sides run the SAME harness.
$root = Join-Path $projectRoot $OutputDirectory
if (Test-Path -LiteralPath $root) { throw 'choose a fresh output directory; previous benchmark evidence is preserved' }
New-Item -ItemType Directory -Path $root | Out-Null
$dspFiles = @('src/VirtualMic.Core/MixBus.cs','src/VirtualMic.Core/CaptureTimeline.cs','plugins/VirtualMic.Essentials/VoiceEffects.cs','plugins/VirtualMic.Essentials/PodcastPlugin.cs','examples/VirtualMic.Delay/DelayPlugin.cs','tests/VirtualMic.Benchmarks/Program.cs')
$sourceHashes = foreach ($file in $dspFiles) { $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(); "$hash  $file" }
$sourceHashes | Set-Content -LiteralPath (Join-Path $root 'dsp-source-sha256.txt') -Encoding ascii
[pscustomobject]@{ baselineRef = $BaselineRef; baselineCommit = (git rev-parse "$BaselineRef^{commit}"); currentParent = (git rev-parse HEAD); workingTreeChanged = [bool](git status --porcelain); utc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'source-metadata.json') -Encoding utf8
$archive = Join-Path $root 'baseline-source.zip'
git archive --format=zip -o $archive $BaselineRef
if ($LASTEXITCODE) { throw 'baseline archive failed' }
$baseline = Join-Path $root 'baseline-source'
Expand-Archive -LiteralPath $archive -DestinationPath $baseline
$baselineHarness = Join-Path $baseline 'tests\VirtualMic.Benchmarks'
New-Item -ItemType Directory -Path $baselineHarness | Out-Null
Copy-Item -LiteralPath tests/VirtualMic.Benchmarks/VirtualMic.Benchmarks.csproj,tests/VirtualMic.Benchmarks/Program.cs,tests/VirtualMic.Benchmarks/packages.lock.json -Destination $baselineHarness
& $dotnet build (Join-Path $baseline 'src\VirtualMic.App\VirtualMic.App.csproj') -c Release -p:RestoreLockedMode=true -p:UseSharedCompilation=false -m:1 | Out-File (Join-Path $root 'baseline-build.txt')
if ($LASTEXITCODE) { throw 'baseline build failed' }
& $dotnet build (Join-Path $baselineHarness 'VirtualMic.Benchmarks.csproj') -c Release -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o (Join-Path $root 'baseline-runner') | Out-File (Join-Path $root 'baseline-harness.txt')
if ($LASTEXITCODE) { throw 'baseline harness failed' }
& $dotnet build src/VirtualMic.App/VirtualMic.App.csproj -c Release -p:RestoreLockedMode=true -p:UseSharedCompilation=false -m:1 | Out-File (Join-Path $root 'current-build.txt')
if ($LASTEXITCODE) { throw 'current build failed' }
& $dotnet build tests/VirtualMic.Benchmarks/VirtualMic.Benchmarks.csproj -c Release -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o (Join-Path $root 'current-runner') | Out-File (Join-Path $root 'current-harness.txt')
if ($LASTEXITCODE) { throw 'current harness failed' }
$baselinePlugins = Join-Path $baseline 'src\VirtualMic.App\bin\Release\net10.0-windows\plugins'
$currentPlugins = Join-Path $projectRoot 'src\VirtualMic.App\bin\Release\net10.0-windows\plugins'
foreach ($round in 1..3) {
    # Alternate ordering so a warming CPU/background task doesn't always favor one side.
    $order = if ($round % 2) { 'baseline','current' } else { 'current','baseline' }
    foreach ($side in $order) {
        $runner = Join-Path $root "$side-runner\VirtualMic.Benchmarks.dll"
        $plugins = if ($side -eq 'baseline') { $baselinePlugins } else { $currentPlugins }
        & $dotnet $runner $plugins (Join-Path $root "$side-$round.json")
        if ($LASTEXITCODE) { throw "$side benchmark failed" }
    }
}
foreach ($side in 'baseline','current') {
    $plugins = if ($side -eq 'baseline') { $baselinePlugins } else { $currentPlugins }
    & $dotnet (Join-Path $root "$side-runner\VirtualMic.Benchmarks.dll") $plugins (Join-Path $root "$side-golden.json") --golden
    if ($LASTEXITCODE) { throw "$side golden fixture failed" }
}
$before = Get-Content -LiteralPath (Join-Path $root 'baseline-golden.json') -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath (Join-Path $root 'current-golden.json') -Raw | ConvertFrom-Json
for ($i = 0; $i -lt 7; $i++) {
    if ($before.rows[$i].audioHash -ne $after.rows[$i].audioHash) { throw ('audio behavior differs: ' + $before.rows[$i].scenario) }
}
$summary = @()
foreach ($scenario in $before.rows.scenario) {
    $values = @{}
    foreach ($side in 'baseline','current') {
        $rows = @(1..3 | ForEach-Object { (Get-Content -LiteralPath (Join-Path $root "$side-$_.json") -Raw | ConvertFrom-Json).rows | Where-Object scenario -eq $scenario })
        $sorted = @($rows.elapsedMs | Sort-Object)
        $values[$side] = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    }
    $summary += [pscustomobject]@{ scenario = $scenario; baselineMs = $values['baseline']; currentMs = $values['current']; improvementPercent = (1 - $values['current'] / $values['baseline']) * 100 }
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'summary.json') -Encoding utf8
$summary | Format-Table
'all seven golden audio/monitor fixtures match byte-for-byte (including gain/mute/monitor/parameter edits).'
"benchmark reports: $root"
