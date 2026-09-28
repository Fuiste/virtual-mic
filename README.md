# virtual mic

a windows soundboard that mixes your microphone and sound clips into a virtual audio cable, with optional monitoring and a configurable effects chain.

**early preview for windows 11 x64.** working on the initial test system; wider device, call-client, and long-session testing is still needed. the portable executable is unsigned.

[download the windows preview](https://github.com/Fuiste/virtual-mic/releases/tag/v0.1.0-preview.1) · [setup guide](docs/setup.md) · [report a problem](https://github.com/Fuiste/virtual-mic/issues)

![virtual mic interface](docs/ui-preview.png)

## included

- select a physical mic, virtual output, and headphones independently.
- import wav, mp3, or aiff by picker or drag and drop; up to 24 pads and two minutes per clip.
- overlap different sounds, retrigger a pad, rename/remove pads, and stop every sound without muting your mic.
- separate microphone, soundboard, master, and headphone levels; dedicated mic mute.
- bass boost and distortion, plus an ordered chain with per-effect mic/sounds/both routing.
- user-defined c# effects with generated controls, a small plugin api, and a buildable delay reference example. [plugin guide](docs/plugins.md).
- soundboard monitoring, with a separate opt-in for hearing your processed mic.
- original synthesized starter tones; no third-party meme recordings bundled.
- keys 1–9 trigger pads while the window is focused; escape stops sounds.
- local saved library, normalized audio copies, and an atomic settings save with a backup.

## download and run

the chain/plugin features are in the **0.2 source preview**. build this checkout to try them; the linked public 0.1 release has the original fixed microphone effects.

download `virtual-mic-v0.1.0-preview.1-win-x64.zip` from the [release page](https://github.com/Fuiste/virtual-mic/releases/tag/v0.1.0-preview.1), extract the entire zip, and open `VirtualMic.exe`. this portable build includes its .net runtime; no sdk is required. keep the accompanying docs, license, and notices with it. windows may identify this unsigned preview as an unrecognized app. the release includes `SHA256SUMS.txt` for verifying the zip and executable.

install vb-cable separately using the [setup guide](docs/setup.md). first startup is silent; the mic is opened only after **start virtual mic**. in a source checkout, the build is under `artifacts/virtual-mic-win-x64/`.

the visual preview contains illustrative sound names. the real library starts empty; import your own clips or add the four generated starter sounds.

## audio setup

**start here: [complete setup guide and first-test checklist](docs/setup.md).** it includes driver installation, discord/slack routing, mute/monitor behavior, troubleshooting, backups, and uninstall steps.

1. install [vb-cable from its publisher](https://vb-audio.com/Cable/), following its administrator/reboot instructions. the app does not install or redistribute this driver.
2. open virtual mic and refresh audio devices.
3. choose your physical microphone under **microphone**.
4. choose **cable input** under **virtual output**. this is the playback side of the cable.
5. select headphones under **monitor**, then enable **listen** if wanted. **hear microphone** also monitors your processed voice; headphones avoid acoustic feedback.
6. click **start virtual mic**.
7. in discord/slack/etc., select the matching **cable output** as the input microphone. leave the call's speaker output on your physical headphones.

the cable's names are counterintuitive: this app sends audio *into* cable input; the chat app captures it *from* cable output. [publisher explanation](https://vb-audio.com/Cable/).

```text
physical mic -> mic/both effects -> gain/mute ---+
                                               +-> master / guard -> cable input
sound files -> pads -> sounds/both effects -----+                          |
                                -> sound gain                  cable output -> chat app

monitor branch -> headphones (sounds only, or sounds + voice)
```

stop the engine before changing devices. microphone/cable failures stop the route; refresh and explicitly restart. headphone failures disable monitoring while the primary route continues. missing saved devices are not silently replaced. virtual endpoints are filtered by common cable/voicemeeter/virtual names; renamed or unusual drivers may need follow-up support. no windows default devices are changed.

## storage and limits

- `%LOCALAPPDATA%\VirtualMic\library.json` stores pad metadata, levels, effects, and endpoint ids. `.bak` retains the prior save. microphone capture is never recorded to disk.
- external plugin folders live in `%LOCALAPPDATA%\VirtualMic\plugins`. plugins are trusted code running inside the app; see [installation, api, and recovery](docs/plugins.md).
- imported files become independent 48 khz stereo floating-point wav copies in `sounds/`; originals are untouched. removing a pad leaves its internal copy recoverable.
- decoded clips have a shared 256 mib memory budget; at most 16 distinct pads sound concurrently. oldest voice is dropped at the limit.
- audio uses shared wasapi, with 30 ms primary and 40 ms monitor buffer requests. these are **not measured end-to-end latency**.
- the output peak guard hard-clamps at 0.98 amplitude (about −0.2 dbfs). sustained overload can still sound distorted; lower the source levels.
- the monitor queue is bounded to 100 ms and drops oldest frames on overflow. extended clock-drift/dropout testing is still required.
- no global hotkeys, tray mode, auto-start, pitch shifting, noise suppression, or echo cancellation in this prototype.

## develop

windows x64 and the .net 10 sdk specified in `global.json` are required. naudio 2.2.1 is pinned deliberately; this is a known api target, not a claim that it is the newest release.

```powershell
.\scripts\build.ps1
.\scripts\build.ps1 -Publish
.\scripts\build-plugin.ps1
.\scripts\verify-ui.ps1
.\scripts\package.ps1
```

the scripts use a project-local sdk at `.tools/dotnet` when present, otherwise `dotnet` on the path. dependency caches, tools, binaries, local audio inventory, and generated test files are ignored by git. regular and publish restores have separate committed lock files. the windows ci workflow builds and tests; it does not publish releases.

the build tests routing, chain order, parameter changes, plugin faults, and concurrent edits, and loads the example dll inside the published executable. `verify-ui.ps1` exercises the actual chain controls, renders populated, empty, minimum-size, and audio-error states, then checks import/resampling and persistence in an isolated library. these checks open no audio devices. `package.ps1` creates a distribution zip and sha256 checksums from an explicit file allowlist, including the plugin sdk dll.

## testing and feedback

see the [local verification results](docs/verification.md) and [stable-release qualification checklist](docs/release-checklist.md). please report your windows version, audio devices, call app, reproduction steps, and whether the meters and monitor worked. review logs before sharing; do not attach private recordings or device ids.

## license

[mit](LICENSE). see [third-party notices](THIRD-PARTY-NOTICES.md) for naudio and the bundled .net runtime. vb-cable is an external dependency with its own license and is not bundled.

an independently named recording device requires an appropriate virtual audio driver. owning that driver adds signing, installer, compatibility, and servicing work; [microsoft's driver-signing requirements](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/driver-signing) apply. vb-cable is an external bridge for this prototype, not our own driver.
