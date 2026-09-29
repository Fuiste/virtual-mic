namespace VirtualMic.PluginApi;

public static class EffectApi { public const int Version = 2; }

public sealed record EffectParameter(string Id, string Name, float Minimum, float Maximum,
    float DefaultValue, string Unit = "", float Step = .01f);

public sealed record EffectDefinition(string Id, string Name, IReadOnlyList<EffectParameter> Parameters,
    int ApiVersion = EffectApi.Version)
{
    // Additive properties preserve the constructor used by compiled v1 plugins.
    public bool MicrophoneOnly { get; init; }
    public bool RequiresSpeakerReference { get; init; }
}

// Optional API v2 contract. Reference is stereo 48 kHz audio from the selected
// physical output, aligned to the microphone capture clock BEFORE any effects.
// The host bypasses this processor when that reference is unavailable.
public interface IReferenceAudioEffect : IAudioEffect
{
    void Process(Span<float> samples, ReadOnlySpan<float> speakerReference,
        IReadOnlyDictionary<string, float> parameters);
}

// API v2: factories with expensive native reset can opt into control-thread
// recreation on bypass/route changes. Reset then only flushes queued output;
// adaptive state may be retained until recreation. Use with mic-only processors.
public interface IRecreateOnEnable { }

// Implement this factory on a public, non-abstract class with a public parameterless
// constructor. Metadata and Create run on the control thread, never the audio thread.
public interface IAudioEffectPlugin
{
    EffectDefinition Definition { get; }
    IAudioEffect Create(int sampleRate, int channels);
}

// Each chain slot owns separate processors for mic and sounds. Process and Reset
// are serialized by the host. Dispose runs after processing has stopped.
public interface IAudioEffect : IDisposable
{
    // Interleaved float32 samples: L,R,L,R at 48 kHz stereo in API v1.
    // Parameters are a validated, read-only snapshot. Do not retain the span,
    // block, allocate, perform I/O, or call the UI from this method.
    void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters);
    // Clear delay/filter history. No I/O or allocation; may run on the audio thread.
    void Reset();
}
