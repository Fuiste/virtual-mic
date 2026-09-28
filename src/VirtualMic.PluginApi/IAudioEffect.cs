namespace VirtualMic.PluginApi;

public static class EffectApi { public const int Version = 1; }

public sealed record EffectParameter(string Id, string Name, float Minimum, float Maximum,
    float DefaultValue, string Unit = "", float Step = .01f);

public sealed record EffectDefinition(string Id, string Name, IReadOnlyList<EffectParameter> Parameters,
    int ApiVersion = EffectApi.Version);

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
