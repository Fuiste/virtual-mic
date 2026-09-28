# changelog

## 0.2.0-preview.1

- ordered effects chain with per-row mic, sounds, or both targets; add/remove, bypass, reorder, and generated parameter controls.
- versioned c# plugin api, folder-based discovery, independent processor state, error reporting, and missing-plugin settings preservation.
- built-in bass boost and distortion use the same api; existing settings migrate to mic-only rows.
- mic mute includes processed voice; stop sounds clears effect tails without stopping the microphone.
- delay reference example, standalone sdk dll, plugin authoring/setup guide, and packaged plugin-loading checks.
- reference delay includes time, feedback, and wet/dry controls, commented dsp code, installation instructions, and six dll-level behavior tests; fully wet startup has no dry leak.

automated checks are recorded in [verification](docs/verification.md). listening/call-client qualification for the new chain remains open.

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
