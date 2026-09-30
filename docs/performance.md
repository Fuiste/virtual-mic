# performance / upcoming 0.4 preview

the performance pass preserves the audio algorithms, parameters, smoothing,
plugin contract, routing, buffers and fault handling. optimization removes
redundant work; it does not change quality modes or combine the two voice plugins.

## measurements / 2026-09-30

local windows x64, amd ryzen 9 9950x3d2 (32 logical processors), .net 10.0.12.
baseline: `v0.3.0-preview.1` / `fc918a0e794c5dc9ee80ae30ddf1ef1248f64fa4`.
both builds use the same benchmark harness, 48 khz stereo and 480-frame / 10 ms
blocks. each pass warms 500 blocks, then renders 2,000 blocks (20 seconds of
synthetic audio) as fast as possible. three alternating baseline/current rounds,
each containing three passes, give nine samples per dsp scenario. the alignment
scenario processes 5,000 blocks / 50 seconds per round.

| scenario | baseline ms | optimized ms | less wall time |
| --- | ---: | ---: | ---: |
| dry mixer | 25.51 | 23.27 | 8.8% |
| bass boost | 45.08 | 33.55 | 25.6% |
| distortion | 34.24 | 27.08 | 20.9% |
| delay | 17.36 | 16.11 | 7.2% |
| podcast voice | 24.41 | 19.35 | 20.7% |
| clean voice | 106.09 | 100.84 | 5.0% |
| all six plugins + 8 overlapping pads + monitor | 198.08 | 157.84 | 20.3% |
| microphone / reference alignment (50 seconds) | 70.16 | 52.61 | 25.0% |

times are medians, for the virtual audio duration above, not milliseconds per
single block. the heavy chain spends roughly 7.9 microseconds per millisecond of
audio on this synthetic render workload. it is already a small load on this pc.
native echo/noise processing remains most of clean voice's cost; its dll and
configuration are unchanged.

the [raw numbered reports, summary and golden checksums](performance-results/README.md)
are retained for inspection, including the source-version caveat for the pre-commit run.

every measured steady dsp render loop allocated **0 managed bytes**, vs 64,000
bytes / 2,000 blocks in the baseline (one level record per block). level snapshots
now allocate on the ui reader. start/play/configuration, exceptional plugin faults
and unusually large first-time buffers can still allocate. native allocations are
outside the managed counter.

these are offline synthetic dsp/timeline measurements. they include synthetic
signal/reference generation and benchmark clock overhead, and exclude actual
wasapi capture/render threads, device drivers, resampling, wpf composition, games,
hotkey delivery and the desktop overlay. wall timings are not whole-app cpu
percent or game-frame benchmarks. reports also contain process cpu time, which
has coarse windows accounting granularity at these short durations. the results
do not guarantee fps, dropout behavior, or savings on another cpu.

## changes

- bass-only processing skips distortion's unused tanh calculations. distortion
  calculates its shared normalization once per stereo frame.
- podcast voice reuses its uncompressed gain below the knee and calculates the
  constant compression slope once per block.
- delay and timestamp-ring loops wrap indexes with a boundary branch; microphone
  reads copy contiguous audio/time spans across at most one wrap.
- mixer gain targets are clamped once per block. disabled monitoring skips its
  sample calculation while retaining the original gain-smoothing state.
- the audio thread publishes numeric levels without per-block records. playing
  ids are copied only when a playback revision changes. minimized meters stop;
  stopped audio stops the main polling timer. the overlay has no animation loop.

## behavior verification and reproduction

seven 20-second golden fixtures match **byte-for-byte** against the released core
and plugins, including primary and monitor audio, gain changes, mic mute,
monitor toggles, and live plugin parameter edits. ordinary dsp, safety, clock,
plugin compatibility and concurrency tests remain in the build.

```powershell
.\scripts\benchmark.ps1
# use a fresh output folder when repeating; prior evidence is retained
.\scripts\benchmark.ps1 -OutputDirectory artifacts/performance-second-run
```

the script exports the baseline from git into an isolated source folder, builds
both versions, and runs the same harness in alternating order. it opens no audio
devices or games and records no microphone audio. reports, cpu/latency samples,
checksums and a median summary stay under the selected artifacts folder.

to investigate actual game-frame impact, compare repeatable scenes with the app
closed, dry passthrough, clean voice, and your normal pads/effects/monitor. measure
frame-time distributions, dropouts and total app cpu while minimizing the control
window. game/device testing, long sessions, mixed dpi and exclusive-input cases
remain qualification work; the benchmarks cannot replace them.
