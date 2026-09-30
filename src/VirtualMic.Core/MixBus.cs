using NAudio.Wave;
using VirtualMic.Core.Effects;

namespace VirtualMic.Core;

public sealed class MixBus : ISampleProvider, IDisposable
{
    private readonly ISampleProvider microphone;
    private readonly EffectCatalog catalog;
    private readonly bool ownsCatalog;
    private readonly EffectChain effects;
    private bool soundsSuppressed = true;
    public MixBus(ISampleProvider microphone, EffectCatalog? catalog = null)
    {
        this.microphone = microphone;
        ownsCatalog = catalog is null;
        this.catalog = catalog ?? new EffectCatalog();
        effects = new(this.catalog);
        effects.Configure(EffectDefaults.FromLegacy(settings));
    }
    private sealed class Voice(string id, float[] samples, float gain)
    {
        public readonly string Id = id;
        public readonly float[] Samples = samples;
        public readonly float Gain = gain;
        public int Position;
    }
    private readonly object gate = new();
    private readonly List<Voice> voices = [];
    private float[] mic = new float[8192];
    private float[] sounds = new float[8192];
    private float[] monitor = new float[8192];
    private float[] reference = new float[8192];
    private AudioSettings settings = new();
    private float levelMic, levelSounds, levelOutput;
    private bool levelLimited;
    private long playbackRevision;
    private float master = .8f;
    private float soundGain = .7f;
    private float micGain = 1;
    private float monitorGain;
    private float hearMic;
    private bool wasMonitoring;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public SampleQueue Monitor { get; } = new();
    public AudioSettings Settings
    {
        get => Volatile.Read(ref settings);
        set
        {
            effects.Configure(value.Effects ?? EffectDefaults.FromLegacy(value));
            Volatile.Write(ref settings, value);
        }
    }
    public bool TryDequeueEffectFault(out EffectFault? fault) => effects.TryDequeueFault(out fault);
    // Publish numbers without allocating on the render thread. Snapshot allocation
    // belongs to the infrequent control/UI reader instead of every audio block.
    public AudioLevels Levels { get { lock (gate) return new(levelMic, levelSounds, levelOutput, levelLimited); } }
    public long PlaybackRevision => Volatile.Read(ref playbackRevision);
    public string[] PlayingIds { get { lock (gate) return voices.Select(v => v.Id).ToArray(); } }
    public int CopyPlayingIds(Span<string?> destination)
    {
        lock (gate)
        {
            int count = Math.Min(destination.Length, voices.Count);
            for (int i = 0; i < count; i++) destination[i] = voices[i].Id;
            return count;
        }
    }

    // A pad retriggers instead of stacking itself. Different pads can overlap.
    public void Play(string id, float[] samples, float gain = 1)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("stereo frames required", nameof(samples));
        lock (gate)
        {
            voices.RemoveAll(v => v.Id == id);
            if (voices.Count >= 16) voices.RemoveAt(0);
            voices.Add(new Voice(id, samples, Math.Clamp(gain, 0, 2)));
            soundsSuppressed = false;
            Interlocked.Increment(ref playbackRevision);
        }
    }

    public void StopSounds()
    {
        lock (gate) { voices.Clear(); soundsSuppressed = true; effects.ResetSounds(); Monitor.Clear(); Interlocked.Increment(ref playbackRevision); }
    }
    public void StopSound(string id) { lock (gate) { if (voices.RemoveAll(v => v.Id == id) > 0) Interlocked.Increment(ref playbackRevision); } }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate) return ReadLocked(buffer, offset, count);
    }

    private int ReadLocked(float[] buffer, int offset, int count)
    {
        if (count % 2 != 0) throw new ArgumentException("stereo frames required", nameof(count));
        if (mic.Length < count)
        {
            mic = new float[count]; sounds = new float[count]; monitor = new float[count]; reference = new float[count];
        }
        Array.Clear(mic, 0, count);
        microphone.Read(mic, 0, count);
        var s = Settings;
        for (int i = 0; i < count; i++) mic[i] = Finite(mic[i]);
        Array.Clear(sounds, 0, count);
        foreach (var voice in voices)
        {
            int length = Math.Min(count, voice.Samples.Length - voice.Position);
            for (int i = 0; i < length; i++) sounds[i] += Finite(voice.Samples[voice.Position + i]) * voice.Gain;
            voice.Position += length;
        }
        if (voices.RemoveAll(v => v.Position >= v.Samples.Length) > 0) Interlocked.Increment(ref playbackRevision);
        bool discontinuity = false;
        bool hasReference = microphone is ISpeakerReferenceSource source && source.CopyReference(reference.AsSpan(0, count), out discontinuity);
        effects.Process(mic, sounds, count, !soundsSuppressed, hasReference ? reference.AsSpan(0, count) : default, discontinuity);
        float micPeak = 0, soundPeak = 0, outputPeak = 0;
        bool limited = false;
        float targetMaster = Math.Clamp(s.MasterGain, 0, 1), targetSound = Math.Clamp(s.SoundGain, 0, 2);
        float targetMic = s.MicMuted ? 0 : Math.Clamp(s.MicGain, 0, 2);
        float targetMonitor = s.MonitorEnabled ? Math.Clamp(s.MonitorGain, 0, 1) : 0;
        float targetHearMic = s.MonitorMic ? 1 : 0;
        for (int i = 0; i < count; i += 2)
        {
            master += (targetMaster - master) * .002f;
            soundGain += (targetSound - soundGain) * .002f;
            micGain += (targetMic - micGain) * .002f;
            monitorGain += (targetMonitor - monitorGain) * .002f;
            hearMic += (targetHearMic - hearMic) * .002f;
            for (int ch = 0; ch < 2; ch++)
            {
                int j = i + ch;
                mic[j] *= micGain;
                float clip = sounds[j] * soundGain;
                float mixed = (mic[j] + clip) * master;
                limited |= Math.Abs(mixed) > .98f;
                buffer[offset + j] = Math.Clamp(Finite(mixed), -.98f, .98f);
                if (s.MonitorEnabled) monitor[j] = Math.Clamp(Finite((mic[j] * hearMic + clip) * master * monitorGain), -.98f, .98f);
                micPeak = Math.Max(micPeak, Math.Abs(mic[j]));
                soundPeak = Math.Max(soundPeak, Math.Abs(clip));
                outputPeak = Math.Max(outputPeak, Math.Abs(buffer[offset + j]));
            }
        }
        if (s.MonitorEnabled) Monitor.Write(monitor, 0, count);
        else if (wasMonitoring) Monitor.Clear();
        wasMonitoring = s.MonitorEnabled;
        levelMic = micPeak; levelSounds = soundPeak; levelOutput = outputPeak; levelLimited = limited;
        return count;
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
    public void Dispose() { effects.Dispose(); if (ownsCatalog) catalog.Dispose(); }
}
