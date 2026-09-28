namespace VirtualMic.Core;

// Immutable snapshots keep the audio callback independent of the UI thread.
public sealed record AudioSettings(
    float MicGain = 1f, float SoundGain = .7f, float MasterGain = .8f,
    bool MicMuted = false, bool MonitorEnabled = false, bool MonitorMic = false,
    float MonitorGain = .5f, bool BassEnabled = false, float BassDb = 6f,
    bool DistortionEnabled = false, float Drive = 3f, float DistortionMix = .4f,
    Effects.EffectSlot[]? Effects = null);

public sealed record AudioLevels(float Mic, float Sounds, float Output, bool Limited);
