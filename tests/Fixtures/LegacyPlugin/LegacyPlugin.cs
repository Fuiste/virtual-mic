using VirtualMic.PluginApi;
public sealed class LegacyPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("fixture.legacy", "legacy plugin", []);
    public IAudioEffect Create(int sampleRate, int channels) => new Gain();
    private sealed class Gain : IAudioEffect
    {
        public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters) { for (int i = 0; i < samples.Length; i++) samples[i] *= 2; }
        public void Reset() { }
        public void Dispose() { }
    }
}
