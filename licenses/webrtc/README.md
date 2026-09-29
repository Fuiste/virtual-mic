# native audio processing notices

These files accompany the win-x64 `webrtc-apm.dll` from
`SoundFlow.Extensions.WebRtc.Apm` 1.4.0. Our build checks its SHA-256 against
`89ab213150e0a3f7d0ca6424ef8f72e2402adabcfae8a369976b8b27df207411`.

Source provenance: SoundFlow commit `c9bcf73512048181f25cb64cd824f4a406f0f96c`,
native submodule `79d02f87a74a173d29a5d4d814893d70262e05f9`:
https://github.com/LSXPrime/webrtc-audio-processing/tree/79d02f87a74a173d29a5d4d814893d70262e05f9

- `COPYING`, `AUTHORS`, `webrtc/LICENSE`, `webrtc/PATENTS`, Ooura, spl_sqrt_floor,
  FFT, PFFFT and RNNoise notices were copied from that native submodule. Filenames
  here replace slashes with hyphens and add `.txt`.
- SoundFlow's MIT license is copied from the NuGet package.
- Abseil uses Apache-2.0; its license was copied from the 20240722.0 source tag
  selected by the native project's `subprojects/abseil-cpp.wrap`.
- The upstream Windows build uses MinGW. GCC's GPLv3 and Runtime Library
  Exception 3.1 texts and mingw-w64's COPYING notices are included for compiler
  runtime components. These were obtained from the respective upstream repos.

The host and our C# plugins are MIT. The native DLL is redistributed unmodified.
The package publisher's native build was consumed, not independently rebuilt;
see `docs/voice-processing.md` for the pinned build recipe and verification limits.
