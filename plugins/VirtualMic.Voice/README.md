# voice cleanup

Two mic-only plugins, running locally on the CPU:

- **echo cancellation** uses the selected physical speaker output as its reference.
  Set Discord/Slack playback to that same output. It works even with listen off.
- **noise suppression** uses WebRTC's high setting, with no hard speech gate or
  automatic gain control. It targets background noise, not speaker identity.

Use echo cancellation before noise suppression, then podcast voice. Each voice
plugin adds 10 ms of buffering and outputs mono voice on both stereo channels.
Re-enabling recreates native state; allow a few seconds for echo adaptation.
The host holds microphone capture by 20 ms to align the speaker reference.

This is WebRTC APM, not Krisp. Nearby speech, loud/clipped speakers and reverberant
rooms remain difficult. Keep the mic close and speaker volume reasonable. Mic
monitoring is disabled during echo cancellation to prevent acoustic feedback.

`webrtc-apm.dll` is the win-x64 native binary from the pinned NuGet package
`SoundFlow.Extensions.WebRtc.Apm` 1.4.0. No SoundFlow audio engine is loaded.
See `docs/voice-processing.md` and `licenses/webrtc/` in the app distribution
for provenance, tests and third-party notices. Our wrapper is MIT.
