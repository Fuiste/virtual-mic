# stable-release qualification

the public `0.3.0-preview.1` build is available for evaluation under the mit license. the items below distinguish completed checks from work still needed before calling a release stable.

## verified locally

- release compilation with warnings treated as errors.
- offline dsp/routing tests: passthrough, overlap, retrigger, mute, monitoring isolation, stop-all, guard, effect scope/response, bounded buffering, concurrent edits, and library persistence.
- read-only windows endpoint discovery.
- silent wpf control rendering and effects-control smoke checks.
- populated, empty, minimum-size, and audio-error ui renders from the self-contained executable.
- 44.1 khz mono import/resampling, invalid-file rejection, starter synthesis, untouched source files, and persisted library/effects in an isolated library.
- official vb-cable pack45 installation, signed driver 3.3.1.7, reboot, healthy windows devices, and discovery of the cable endpoints.
- real cable/monitor tone verification, monitor toggle/recovery, failed-monitor isolation, and engine stop/restart on the initial windows 11 x64 / komplete audio 6 system.
- user listening confirmed mic and monitoring work on that system.

## remaining audio qualification

- verify 0.3 echo cancellation with speakers, noise suppression with real voice, podcast compression, and unfiltered soundboard clips in Discord/Slack. synthetic and capture-only results are in `verification.md`; real-room quality is pending.

- qualify the 0.3 chain and revised capture path by listening with mic/sounds/both targets, multiple ordered effects, and the external delay plugin; verify tails, mute, and sounds-only monitoring in real calls. existing hardware results below predate this change.
- measure cpu/dropouts and long sessions with realistic third-party chains; in-process plugins must be trusted and can affect app stability.

- confirm simultaneous microphone + clips through the cable in real call clients on additional systems.
- verify discord and slack with their voice processing enabled and disabled; establish recommended settings based on recorded results.
- test headphones-only monitoring and voice monitoring separately, including rapid toggles and stop/restart.
- test mono/stereo inputs at 44.1/48/96 khz and independent output rates; include usb interface, usb mic, onboard audio, and bluetooth if supported.
- measure onset latency, mic latency, cpu use, glitches, and 60-minute clock drift under normal mixed usage.
- unplug/replug each endpoint, change default devices, sleep/resume, deny mic permissions, and quit while playing.
- test corrupt, truncated, long, silent, loud, and unsupported files; verify memory stays bounded at the library cap.
- verify on a fresh windows 11 x64 machine with no development sdk; establish any additional supported os/architectures.

## distribution

- selected for the preview: mit license; `virtual-mic` repository.
- confirm external-driver onboarding vs a separately signed first-party driver. driver redistribution rights must be explicitly covered; do not bundle vb-cable by default.
- add application icon, versioned release notes, binary hashes, signing, and an installer/uninstaller that preserves user libraries unless removal is requested.
- assess keyboard navigation, screen-reader naming, high contrast, and multiple dpi scales on real displays.
- decide whether global hotkeys and tray/background operation are required for v1.
- include dependency licenses and runtime notices with distributed binaries.
- run dependency/security checks and inspect the final public source tree for local paths, device ids, audio, logs, or credentials.
- publish stable releases only after the remaining qualification is complete; keep evaluation builds marked as prereleases.
