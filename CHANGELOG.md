# changelog

## 0.1.0-preview.1

first public windows x64 preview.

- microphone passthrough and up to 24 sound pads routed to an external virtual audio cable.
- wav, mp3, and aiff import, original sample tones, pad rename/remove, retrigger, and overlapping playback.
- independent microphone, sound, master, and monitor levels; mic mute and stop-sounds controls.
- microphone bass boost and distortion; optional sound and voice monitoring.
- compact native interface with saved device choices and a local sound library.
- fixed the initial monitor buffer conversion failure; headphone failures keep the primary route running.
- local error diagnostics, setup documentation, dependency notices, and portable packaging.

validated on one windows 11 x64 system with komplete audio 6 and vb-cable. core tests, silent ui/library checks, a local cable/monitor tone test, and user listening passed. other hardware, call clients, unplug/sleep recovery, and long sessions still need wider testing. the portable executable is unsigned. see [setup](docs/setup.md) and [qualification](docs/release-checklist.md).
