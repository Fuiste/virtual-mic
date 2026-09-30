# effects and plugins / api v2

the 0.3 preview bundles all effects as external plugins, with mic-only voice cleanup and a speaker-reference contract. [voice setup](voice-processing.md). compiled api v1 plugins from 0.2 remain supported.

## use the chain

choose an effect beside **effects**, then click **+**. each row has an enable checkbox, target, up/down buttons, and remove button. audio passes through enabled rows **top to bottom**. parameter controls come from the plugin's metadata. up to 16 rows are supported, including repeated instances of the same effect.

| target | affected audio |
| --- | --- |
| mic | physical microphone only |
| sounds | the sum of playing sound pads |
| both | microphone and sounds, processed independently |

“both” creates separate state for the two sources. an echo of your voice stays out of sounds-only monitoring. this is two matching source chains, rather than a single effect on the finished mix. compression or distortion can therefore sound different from processing the combined signal.

```text
mic -> enabled mic/both rows -> mic gain/mute --+
                                             +-> master -> peak guard -> cable
pads -> sum -> enabled sounds/both rows ------+
                              -> sound gain

monitor: processed sounds + optional processed mic -> master -> monitor gain
```

mic mute includes microphone effect tails. **stop sounds** clears sound-effect state and suppresses that branch until another pad is played; microphone effects continue. natural pad endings let echo/reverb tails finish. removing one pad does not reset shared sound-effect tails. disabling a row or removing a target resets the affected processor. reordering and parameter changes preserve its state. routing/bypass/reorder edits are immediate and can click; the host does not crossfade chain edits.

bass boost and distortion now ship in `plugins/essentials` beside the executable. delay and the voice-cleanup plugins are also bundled; the core registers no built-in effects. existing libraries migrate to those two rows, in that order, targeting mic with the same enabled states and values. an intentionally empty chain stays empty. unknown effect ids and their parameters stay in the saved library so reinstalling a plugin restores the row. 0.3 saves use library format 2; the upcoming 0.4 build writes format 3 for [gaming settings](gaming.md). older apps refuse unsupported formats instead of overwriting them. back up the library before testing and restore a pre-upgrade backup to downgrade while the app is closed. the plugin api remains v2 with compiled v1 compatibility.

## install or remove a plugin

1. click **plugins → open plugins folder**. the folder is `%LOCALAPPDATA%\VirtualMic\plugins`.
2. close virtual mic. copy each plugin into its own subfolder, keeping its manifest, dll, dependency manifest, and private dependencies together.
3. reopen the app. choose the effect in the add menu. **plugins** lists loaded effects and any load errors.
4. to update/remove a plugin, close the app and replace/remove that plugin's folder, then reopen it. a missing plugin's existing rows are bypassed and preserved.

```text
plugins/
  delay/
    plugin.json
    VirtualMic.Delay.dll
    VirtualMic.Delay.deps.json
    ...private dependencies, if any
```

plugins are **trusted, in-process .net code** with the same windows permissions as the app. this is not a sandbox, permission system, or vst host. constructors execute during loading. managed processing exceptions and non-finite samples bypass the faulty row and display an error; the current processing chunk is restored to its input. earlier chunks in that buffer may already have been processed. stop/start audio to retry the row. a plugin that hangs, crashes native code, or exhausts resources can still affect the entire app. if one prevents startup, close the app and move its folder out of `plugins` before reopening.

## build the example

delay is included in 0.3 under `plugins/delay`; choose it from the add menu. no sdk is needed. the separate delay archive remains available for users of 0.2.

install the .net 10 sdk and clone this repo. from the repo root:

```powershell
.\scripts\build-plugin.ps1
```

this builds [the delay reference example](../examples/VirtualMic.Delay/DelayPlugin.cs) into `artifacts/sample-plugins/delay/`; it does not install it. to test a modified bundled delay, close the app and replace its `plugins/delay` folder. for a new effect, change its assembly and effect ID before installing it in the user plugins folder. delay exposes time, feedback, and mix. it smooths feedback/mix changes; moving delay time can produce an audible discontinuity. it is a small example to extend, rather than a studio delay algorithm.

the [example readme](../examples/VirtualMic.Delay/README.md) covers control ranges, installation, signal flow, and extension points. its six dll tests cover repeat timing, dry/wet balance, feedback decay/wraparound, parameter edits/reset, independent state, and extreme settings. a fully wet instance starts with delayed audio only; the first parameter snapshot applies immediately.

to develop your own, copy the example project, give its assembly and manifest a new name, and change its effect id to a unique stable value such as `yourname.robot`. the project references only the [plugin api](../src/VirtualMic.PluginApi/IAudioEffect.cs). it needs no app/core or naudio reference. use a public factory with a public parameterless constructor; one dll may export multiple factories.

```csharp
using VirtualMic.PluginApi;

public sealed class GainPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("yourname.gain", "gain", [
        new("gain", "gain", 0, 2, 1, "x", .01f)
    ]);

    public IAudioEffect Create(int sampleRate, int channels) => new GainProcessor();
}

internal sealed class GainProcessor : IAudioEffect
{
    private float gain = 1;
    public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
    {
        float target = parameters["gain"];
        for (int i = 0; i < samples.Length; i += 2)
        {
            gain += (target - gain) * .002f;
            samples[i] *= gain;
            samples[i + 1] *= gain;
        }
    }
    public void Reset() => gain = 1;
    public void Dispose() { }
}
```

the manifest beside the compiled dll is:

```json
{ "apiVersion": 1, "assembly": "YourPlugin.dll" }
```

the assembly field is a filename in that folder, not an absolute path. the loader scans up to 64 immediate plugin subfolders; manifests are limited to 16 kib. malformed manifests, unsupported api versions, invalid metadata, duplicate ids, or failing factories appear in the plugins dialog. built-in ids cannot be replaced.

## develop outside this repo

the 0.3 portable build includes `plugin-sdk/VirtualMic.PluginApi.dll`. reference it from a normal .net class library; adjust this hint path to your extracted build:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="VirtualMic.PluginApi">
      <HintPath>../plugin-sdk/VirtualMic.PluginApi.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

run `dotnet build -c Release`, then distribute the plugin output, manifest, `.deps.json`, and its private dependencies together. do not bundle a .net runtime or the host's api/core/app assemblies. use managed dependencies compatible with net10.0; native dependencies must match windows x64 and be included with their required license notices. each plugin folder has its own assembly load context; the api contract is always shared with the host. [microsoft's plugin loading guide](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support) explains the dependency-resolution pattern.

## processing contract

- `EffectApi.Version` is **2**; the host accepts manifests/definitions with api versions 1 and 2. the contract assembly remains `1.0.0.0`, and its existing constructor and interfaces retain binary compatibility. new voice capabilities require manifest/definition version 2. incompatible versions are rejected. keep effect/parameter ids stable across updates; adding parameters with defaults is supported.
- effect and parameter ids match `[a-z0-9][a-z0-9._-]{0,79}`. effect names are 1–80 characters; parameter names 1–40; units up to 16. at most 16 uniquely named numeric parameters per effect. bounds/default/step must be finite; min < max; default is within bounds; step > 0.
- all processors receive **48,000 hz, two channels**, interleaved float32 (`L, R, L, R`). buffers contain complete stereo frames; chunk lengths vary and are at most 2,048 samples (1,024 frames). write in place, keep the sample count/rate/channels unchanged, return finite samples. the downstream peak guard contains output amplitude; it does not make excessive plugin gain sound good.
- parameters are immutable snapshots for that call, with missing/invalid values replaced by defaults and out-of-range values clamped. implement smoothing yourself where needed. no custom gui, strings, meters, events, latency reporting, or plugin-specific opaque state is provided in v1.
- `Create` runs on the control thread and must return a **new instance each time**. allocate buffers there. keep startup quick. ordinary rows own two instances, even when only one target is active. mic-only rows own one. repeated rows also get independent instances.
- `Process` runs on one render thread. do no disk/network/ui work, blocking waits, or per-block allocations. do not retain the span, change another instance's state, or invoke the host. .net is not a hard realtime environment; inefficient plugins can underrun audio.
- `Reset` clears tails/filter history and may run on the render or control thread, serialized with processing. avoid allocation, blocking, and i/o here too. it is called for bypass/routing removal and **stop sounds** on the sound branch.
- `Dispose` runs on the control thread after that instance can no longer process. release owned resources without waiting on ui callbacks. factories and disposal must not touch shared audio device state.
- the host stores enable state, order, target and parameter values in `library.json`. runtime delay/filter history is not saved. all instances are recreated when audio restarts.

## verify before sharing

test silence, impulses, stereo independence, partial blocks, parameter extremes, repeated rows, mic/sounds/both, sounds-only monitoring, bypass, reorder, stop sounds, mic mute, and engine restart. watch cpu usage and listen for discontinuities. a source-only plugin should remain silent with silent input unless generation is its intended function.

`scripts/build.ps1 -Publish` runs the core/routing tests, builds the example, then loads and processes that dll inside the actual self-contained single-file executable without opening devices. `scripts/verify-ui.ps1` exercises add/remove/reorder/bypass/target/parameter controls and saved settings in an isolated library. the optional hardware smoke test and a real call-client listening test remain separate checks; see [verification](verification.md).


## bundled loading and api v2 voice capabilities

the host loads `plugins/*/plugin.json` beside the exe first, then the user folder.
first registration wins: a user plugin cannot silently replace a bundled ID.
duplicate IDs appear in **plugins**; remove an old user-installed delay if it
conflicts with the newly bundled copy. keep the full app folder together when
upgrading. removing a bundled DLL bypasses its saved rows; there is no hidden
fallback implementation. plugin source lives in `plugins/` and `examples/`.

`EffectDefinition` adds two init-only properties without changing the v1
constructor. set `MicrophoneOnly = true` to restrict a voice processor to the mic.
set `RequiresSpeakerReference = true` as well for echo cancellation; the factory
must return `IReferenceAudioEffect`. these properties require `ApiVersion: 2`.

```csharp
public EffectDefinition Definition { get; } = new("yourname.echo", "my echo", [], ApiVersion: 2)
    { MicrophoneOnly = true, RequiresSpeakerReference = true };
```

`IReferenceAudioEffect.Process(samples, speakerReference, parameters)` receives
same-length stereo spans at 48 kHz, aligned by microphone/speaker capture timestamps.
the reference is the selected physical output, before plugin processing, and
includes other apps' playback. a timestamp PLL smooths packet jitter and reference
lookup interpolates across independent device clocks. real gaps become silence;
missing/failed capture bypasses the reference-dependent processor. the host calls
`Reset` when the reference disappears or capture continuity changes. the current
contract has no general latency compensation: order echo before nonlinear/delaying
plugins, as the clean-voice preset does. speaker capture is host-owned, only opened
when a reference effect is enabled, and independent of the listen toggle.

native processors with expensive initialization can implement `IRecreateOnEnable`.
the host then recreates them on the control thread when bypass/target changes.
their `Reset` must still flush pending output without blocking or allocation, but
may retain adaptive state until recreation. the bundled WebRTC plugins use this
exception to avoid native initialization inside the render callback. ordinary
v1 processors retain the original full-history reset contract.

`plugins/VirtualMic.Voice` demonstrates private native dependencies and 10 ms
frame adaptation. `plugins/VirtualMic.Essentials/PodcastPlugin.cs` is a fully
managed mic-only example. prefer the delay example for a small general-purpose
starter. native libraries can be included beside the plugin DLL; only simple
filenames are resolved from that folder when the dependency manifest has no entry.
