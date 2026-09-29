using VirtualMic.PluginApi;

namespace VirtualMic.Essentials;

public sealed class BassBoostPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("virtualmic.bass", "bass boost",
        [new("gain", "boost", 0, 18, 6, "db", .1f)]);
    public IAudioEffect Create(int sampleRate, int channels) => new CharacterProcessor(true);
}

public sealed class DistortionPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("virtualmic.distortion", "distortion",
        [new("drive", "drive", 1, 20, 3, "×", .1f), new("mix", "mix", 0, 100, 40, "%", 1)]);
    public IAudioEffect Create(int sampleRate, int channels) => new CharacterProcessor(false);
}

internal sealed class CharacterProcessor(bool bass) : IAudioEffect
{
    private readonly VoiceEffects processor = new();
    public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
    {
        processor.Process(samples, bass ? parameters["gain"] : 0,
            bass ? 1 : parameters["drive"], bass ? 0 : parameters["mix"] / 100);
    }
    public void Reset() => processor.Reset();
    public void Dispose() { }
}
