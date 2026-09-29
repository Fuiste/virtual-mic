# third-party components

- [naudio 2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1), copyright mark heath and contributors, mit license. the original notice is in `licenses/naudio.txt`.
- [.net runtime](https://github.com/dotnet/runtime) and [windows desktop runtime](https://github.com/dotnet/wpf), copyright .net foundation and contributors, included in the self-contained executable. original license and third-party notices are in `licenses/`.
- bass shelf coefficients implement the standard low-shelf equations described in the [audio eq cookbook](https://www.w3.org/TR/audio-eq-cookbook/). implementation is local; no source file copied.
- vb-cable is separately installed software from vb-audio. no vb-cable installer, driver, or license is included or implied by this project.

virtual mic is licensed under the mit license in `LICENSE`. third-party components retain their own licenses and notices.

## bundled voice processing (0.3)

The echo-cancellation and noise-suppression plugins redistribute the unmodified
win-x64 `webrtc-apm.dll` from SoundFlow.Extensions.WebRtc.Apm 1.4.0 (NuGet).
This uses WebRTC / PulseAudio audio-processing code, Abseil, and the upstream
Windows compiler runtime. The complete collected notices, WebRTC patent grant,
and source/provenance details are in [licenses/webrtc](licenses/webrtc/README.md).
The build checks the DLL SHA-256; no managed SoundFlow audio engine is bundled.
Our C# plugin implementations are covered by this repository's MIT license.
