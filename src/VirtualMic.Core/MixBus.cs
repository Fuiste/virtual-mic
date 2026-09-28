using NAudio.Wave;

namespace VirtualMic.Core;

public sealed class MixBus(ISampleProvider microphone) : ISampleProvider
{
    private sealed class Voice(string id, float[] samples, float gain)
    {
        public readonly string Id = id;
        public readonly float[] Samples = samples;
        public readonly float Gain = gain;
        public int Position;
    }
    private readonly object gate = new();
    private readonly List<Voice> voices = [];
    private readonly VoiceEffects effects = new();
    private float[] mic = new float[8192];
    private float[] sounds = new float[8192];
    private float[] monitor = new float[8192];
    private AudioSettings settings = new();
    private AudioLevels levels = new(0, 0, 0, false);
    private float master = .8f;
    private float soundGain = .7f;
    private float monitorGain;
    private float hearMic;
    private bool wasMonitoring;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public SampleQueue Monitor { get; } = new();
    public AudioSettings Settings { get => Volatile.Read(ref settings); set => Volatile.Write(ref settings, value); }
    public AudioLevels Levels => Volatile.Read(ref levels);
    public string[] PlayingIds { get { lock (gate) return voices.Select(v => v.Id).ToArray(); } }

    // A pad retriggers instead of stacking itself. Different pads can overlap.
    public void Play(string id, float[] samples, float gain = 1)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("stereo frames required", nameof(samples));
        lock (gate)
        {
            voices.RemoveAll(v => v.Id == id);
            if (voices.Count >= 16) voices.RemoveAt(0);
            voices.Add(new Voice(id, samples, Math.Clamp(gain, 0, 2)));
        }
    }

    public void StopSounds() { lock (gate) voices.Clear(); Monitor.Clear(); }
    public void StopSound(string id) { lock (gate) voices.RemoveAll(v => v.Id == id); }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count % 2 != 0) throw new ArgumentException("stereo frames required", nameof(count));
        if (mic.Length < count)
        {
            mic = new float[count]; sounds = new float[count]; monitor = new float[count];
        }
        Array.Clear(mic, 0, count);
        microphone.Read(mic, 0, count);
        var s = Settings;
        effects.Process(mic, count, s);
        Array.Clear(sounds, 0, count);
        lock (gate)
        {
            foreach (var voice in voices)
            {
                int length = Math.Min(count, voice.Samples.Length - voice.Position);
                for (int i = 0; i < length; i++) sounds[i] += VoiceEffects.Finite(voice.Samples[voice.Position + i]) * voice.Gain;
                voice.Position += length;
            }
            voices.RemoveAll(v => v.Position >= v.Samples.Length);
        }
        float micPeak = 0, soundPeak = 0, outputPeak = 0;
        bool limited = false;
        for (int i = 0; i < count; i += 2)
        {
            master += (Math.Clamp(s.MasterGain, 0, 1) - master) * .002f;
            soundGain += (Math.Clamp(s.SoundGain, 0, 2) - soundGain) * .002f;
            monitorGain += ((s.MonitorEnabled ? Math.Clamp(s.MonitorGain, 0, 1) : 0) - monitorGain) * .002f;
            hearMic += ((s.MonitorMic ? 1f : 0f) - hearMic) * .002f;
            for (int ch = 0; ch < 2; ch++)
            {
                int j = i + ch;
                float clip = sounds[j] * soundGain;
                float mixed = (mic[j] + clip) * master;
                limited |= Math.Abs(mixed) > .98f;
                buffer[offset + j] = Math.Clamp(VoiceEffects.Finite(mixed), -.98f, .98f);
                monitor[j] = Math.Clamp(VoiceEffects.Finite((mic[j] * hearMic + clip) * master * monitorGain), -.98f, .98f);
                micPeak = Math.Max(micPeak, Math.Abs(mic[j]));
                soundPeak = Math.Max(soundPeak, Math.Abs(clip));
                outputPeak = Math.Max(outputPeak, Math.Abs(buffer[offset + j]));
            }
        }
        if (s.MonitorEnabled) Monitor.Write(monitor, 0, count);
        else if (wasMonitoring) Monitor.Clear();
        wasMonitoring = s.MonitorEnabled;
        Volatile.Write(ref levels, new AudioLevels(micPeak, soundPeak, outputPeak, limited));
        return count;
    }
}
