using VirtualMic.PluginApi;

namespace VirtualMic.Delay;

// The host discovers this public factory and generates controls from the metadata.
// Keep ids stable when extending the example; saved chains refer to these ids.
public sealed class DelayPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("example.delay", "delay", [
        new("delay", "time", 30, 1000, 240, "ms", 1),
        new("feedback", "feedback", 0, 85, 35, "%", 1),
        new("mix", "mix", 0, 100, 30, "%", 1)
    ], ApiVersion: 1);

    // Each call returns independent state for one chain row and one source bus.
    public IAudioEffect Create(int sampleRate, int channels) => new DelayProcessor(sampleRate, channels);
}

internal sealed class DelayProcessor : IAudioEffect
{
    private readonly float[] history;
    private readonly int sampleRate, channels, frames;
    private int position;
    private float wet, feedback;
    private bool initialized;

    public DelayProcessor(int sampleRate, int channels)
    {
        this.sampleRate = sampleRate; this.channels = channels;
        // Allocate the maximum one-second delay once, off the audio thread.
        // The extra frame keeps read and write positions distinct at the maximum.
        frames = sampleRate + 1;
        history = new float[frames * channels];
    }

    public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
    {
        // The host supplies finite, clamped parameter snapshots and complete frames.
        int distance = Math.Clamp((int)(sampleRate * parameters["delay"] / 1000), 1, frames - 1);
        float targetWet = parameters["mix"] / 100;
        float targetFeedback = parameters["feedback"] / 100;
        if (!initialized)
        {
            // Honor the first settings immediately: 100% wet must not leak dry audio.
            wet = targetWet; feedback = targetFeedback; initialized = true;
        }

        for (int i = 0; i < samples.Length; i += channels)
        {
            // Smooth gain edits per frame; all channels use the same coefficients.
            wet += (targetWet - wet) * .002f;
            feedback += (targetFeedback - feedback) * .002f;
            int read = (position - distance + frames) % frames;
            for (int ch = 0; ch < channels; ch++)
            {
                float dry = samples[i + ch];
                float delayed = history[read * channels + ch];
                // Feedback recirculates within the same channel, independently of mix.
                history[position * channels + ch] = dry + delayed * feedback;
                samples[i + ch] = dry * (1 - wet) + delayed * wet;
            }
            position = (position + 1) % frames;
        }
    }

    // Bypass, routing changes and stop-sounds clear tails without reallocating.
    public void Reset() { Array.Clear(history); position = 0; initialized = false; }
    public void Dispose() { } // Only managed memory is owned by this processor.
}
