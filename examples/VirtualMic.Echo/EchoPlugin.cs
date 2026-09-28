using VirtualMic.PluginApi;

namespace VirtualMic.Echo;

public sealed class EchoPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("example.echo", "echo", [
        new("delay", "delay", 30, 1000, 240, "ms", 1),
        new("feedback", "feedback", 0, 85, 35, "%", 1),
        new("mix", "mix", 0, 100, 30, "%", 1)
    ]);
    public IAudioEffect Create(int sampleRate, int channels) => new EchoProcessor(sampleRate, channels);
}

internal sealed class EchoProcessor : IAudioEffect
{
    private readonly float[] delay;
    private readonly int sampleRate, channels, frames;
    private int position;
    private float wet = .3f, feedback = .35f;

    public EchoProcessor(int sampleRate, int channels)
    {
        this.sampleRate = sampleRate; this.channels = channels;
        frames = sampleRate + 1;
        delay = new float[frames * channels];
    }

    public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
    {
        int distance = Math.Clamp((int)(sampleRate * parameters["delay"] / 1000), 1, frames - 1);
        float targetWet = parameters["mix"] / 100;
        float targetFeedback = parameters["feedback"] / 100;
        for (int i = 0; i < samples.Length; i += channels)
        {
            wet += (targetWet - wet) * .002f;
            feedback += (targetFeedback - feedback) * .002f;
            int read = (position - distance + frames) % frames;
            for (int ch = 0; ch < channels; ch++)
            {
                float dry = samples[i + ch];
                float echo = delay[read * channels + ch];
                delay[position * channels + ch] = dry + echo * feedback;
                samples[i + ch] = dry * (1 - wet) + echo * wet;
            }
            position = (position + 1) % frames;
        }
    }

    public void Reset() { Array.Clear(delay); position = 0; }
    public void Dispose() { }
}
