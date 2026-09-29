# voice cleanup / 0.3 preview

## start here

1. extract the **entire** portable zip. keep `plugins` beside `VirtualMic.exe`.
2. choose your physical microphone and **cable input** as usual.
3. under **speakers / headphones**, select the physical output your call app plays
   through. echo cancellation uses that output even when **listen** is off.
4. click **clean voice**, then **start virtual mic**. this adds/enables three mic
   effects in order: echo cancellation, noise suppression, podcast voice. it keeps
   your other rows and their settings; repeated clicks do not add duplicate rows.
5. in discord, select **cable output** as the microphone and the same physical
   speakers as playback. disable discord's noise suppression, echo cancellation
   and automatic gain control for this input. set input sensitivity manually so
   quiet clips pass, or use push-to-talk while playing clips. other call clients
   may need their music/original-audio mode to stop filtering the mixed signal.
6. speak, then play a pad. use a second device or another listener to check both.
   allow several seconds of speaker audio for the echo canceller to adapt.

for headphones, bypass **echo cancellation**; leave noise suppression and podcast
voice enabled if useful. **hear microphone** is disabled while echo cancellation
is enabled, to avoid feeding your own voice back through speakers. **listen** still
monitors the pads. changing the selected output requires stopping/restarting audio.

## what each plugin does

| plugin | controls | purpose |
| --- | --- | --- |
| podcast voice | compression, output | rumble filter, soft-knee compressor, modest makeup gain and peak limiting |
| echo cancellation | on/off | removes speaker audio picked up by the mic, using a WASAPI loopback reference |
| noise suppression | on/off | reduces background noise with WebRTC's high suppression setting |
| bass boost | boost | the previous low shelf, now an external plugin |
| distortion | drive, mix | the previous saturation effect, now an external plugin |
| delay | time, feedback, mix | the reference stereo delay, now included |

the three voice plugins are **mic-only**, enforced in the host even for manually
edited saved settings. pads join the mix afterward. bass, distortion and delay
retain mic/sounds/both routing. existing bass/distortion IDs, values and enable
states survive the move to external DLLs; voice cleanup is opt-in.

## limitations and troubleshooting

- this uses [WebRTC APM](https://github.com/LSXPrime/SoundFlow/tree/c9bcf73512048181f25cb64cd824f4a406f0f96c/Extensions/SoundFlow.Extensions.WebRtc.Apm),
  **not krisp**, and does not recognize a particular speaker. fans and steady
  noise are easier than nearby speech, music and room reverberation. it does not
  promise to remove every keyboard click or background speaker.
- cancellation needs the same physical output the other voices/music play through.
  sound from a separate output, a television or another computer has no reference.
  clipped speakers, very loud playback or the wrong output can leave echo. keep
  the mic close and reduce speaker volume before increasing suppression/gain.
- if the reference device disappears, capture fails, or its timestamps are invalid,
  the app reports that echo cancellation is bypassed. the mic and pads continue.
  toggle echo cancellation off/on to retry a failed speaker capture; stop/start
  audio for invalid microphone timestamps. select another output while stopped
  if necessary. speaker silence is a valid silent reference.
- microphone capture holds about 20 ms to let loopback packets arrive. echo and
  noise plugins each add 10 ms of frame buffering. WASAPI/device/cable/call buffers
  add more; this is not a measured end-to-end latency figure.
- voice cleanup outputs the average of the mic's channels as mono on both sides.
  bass/distortion/delay retain stereo. if a stereo interface has only one connected
  mic channel, this downmix reduces its level; use mic gain/podcast output as needed.
- echo should precede nonlinear or delaying effects. the preset orders the chain
  for that; custom reordering can make cancellation less effective.
- processing is local and uses no cloud API. speaker capture and microphone audio
  stay in memory. the app does not record either stream or change windows defaults.

## implementation and dependency provenance

`plugins/VirtualMic.Essentials` contains the original effects and managed compressor.
`plugins/VirtualMic.Voice` contains independent C# plugins over a small native C ABI.
all are built as external DLLs and discovered through the same public plugin API.
see [the authoring guide](plugins.md) for v2 mic-only/reference capabilities and v1
binary compatibility. per-row state and native processors remain independent.

the native dependency is the **win-x64** `webrtc-apm.dll` in NuGet
`SoundFlow.Extensions.WebRtc.Apm` **1.4.0**. `PackageDownload` deliberately excludes
the managed SoundFlow engine and transitive runtime assets. builds verify SHA-256:

```text
89ab213150e0a3f7d0ca6424ef8f72e2402adabcfae8a369976b8b27df207411
```

source package repository commit:
`c9bcf73512048181f25cb64cd824f4a406f0f96c`; its native submodule is
`79d02f87a74a173d29a5d4d814893d70262e05f9` (webrtc-audio-processing 2.1).
the upstream recipe uses MinGW and Abseil 20240722.0. this repo consumes the
publisher's prebuilt binary; it does not claim a reproducible local native rebuild.
its import table lists `KERNEL32.dll`, `ucrtbase.dll` and `WINMM.dll` only.
notices, WebRTC's patent grant and bundled third-party/compiler-runtime license
texts are in `licenses/webrtc`. our plugin code remains MIT.

## verification

`scripts/build.ps1 -Publish` tests the actual bundled DLLs, including native calls
inside the self-contained app. synthetic checks cover compressor dynamic range,
steady-noise attenuation, delayed echo, simultaneous voiced test tone, partial frame
sizes, zero managed frame allocations, mic-only routing, missing reference bypass,
clock drift, gaps, bounded backlog, and a plugin compiled against the frozen v1 API.

synthetic results are regression checks, not room-quality guarantees. real-room
listening, discord clips, device changes, and long sessions remain release checks.
the opt-in `VirtualMic.exe --verify-capture <report.json>` diagnostic briefly opens
the saved mic and speaker capture devices, writes only counters/errors, and plays
or saves no audio. it is not part of CI and does not measure cancellation quality.
