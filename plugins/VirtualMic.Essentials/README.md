# essentials

Bundled external plugins: bass boost, distortion and podcast voice. The first two
retain their v0.2 IDs and parameters, so saved chains migrate without changes.
Podcast voice is mic-only: 80 Hz high-pass, linked stereo soft-knee compressor,
5 ms attack, 140 ms release, modest makeup gain and a rounded peak limiter.
Add it and use the defaults, then adjust compression or output if needed.

Source: `plugins/VirtualMic.Essentials`. MIT; see the app's LICENSE.
