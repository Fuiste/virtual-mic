using VirtualMic.PluginApi;

namespace VirtualMic.Voice;

public sealed class EchoCancellationPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("virtualmic.echo-cancellation", "echo cancellation", [])
        { MicrophoneOnly = true, RequiresSpeakerReference = true };
    public IAudioEffect Create(int sampleRate, int channels) => new VoiceProcessor(sampleRate, channels, echo: true);
}

public sealed class NoiseSuppressionPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("virtualmic.noise-suppression", "noise suppression", [])
        { MicrophoneOnly = true };
    public IAudioEffect Create(int sampleRate, int channels) => new VoiceProcessor(sampleRate, channels, echo: false);
}

// Voice is downmixed to mono and returned identically to L/R. A 10 ms adapter
// accepts arbitrary host blocks; buffered output adds exactly 10 ms per plugin.
// Native state is allocated/warmed by Create. Re-enable rebuilds on the control
// thread. Reset only flushes the adapter, never initializes native DSP in render.
internal sealed unsafe class VoiceProcessor : IReferenceAudioEffect, IRecreateOnEnable
{
    private readonly bool echo;
    private nint apm, stream;
    private readonly float[] input = new float[480], reference = new float[480], output = new float[480];
    private int position;
    public VoiceProcessor(int sampleRate, int channels, bool echo)
    {
        if (sampleRate != 48000 || channels != 2) throw new NotSupportedException("48 kHz stereo required");
        this.echo = echo;
        try
        {
            apm = NativeApm.Create(); stream = NativeApm.StreamCreate(48000, 1);
            if (apm == 0 || stream == 0) throw new InvalidOperationException("voice processor allocation failed");
            nint config = NativeApm.ConfigCreate();
            if (config == 0) throw new InvalidOperationException("voice configuration allocation failed");
            try
            {
                NativeApm.Echo(config, echo ? 1 : 0, 0);
                NativeApm.Noise(config, echo ? 0 : 1, 2); // high; no AGC or hard speech gate
                NativeApm.HighPass(config, 1);
                Check(NativeApm.Apply(apm, config)); Check(NativeApm.Initialize(apm));
            }
            finally { NativeApm.ConfigDestroy(config); }
            Frame(); Reset();
        }
        catch { Dispose(); throw; }
    }
    public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
    {
        if (echo) throw new InvalidOperationException("speaker reference required");
        Process(samples, ReadOnlySpan<float>.Empty, parameters);
    }
    public void Process(Span<float> samples, ReadOnlySpan<float> speakerReference, IReadOnlyDictionary<string, float> parameters)
    {
        if (echo && speakerReference.Length != samples.Length) throw new ArgumentException("reference length differs");
        for (int i = 0; i < samples.Length; i += 2)
        {
            input[position] = (samples[i] + samples[i + 1]) * .5f;
            reference[position] = echo ? (speakerReference[i] + speakerReference[i + 1]) * .5f : 0;
            samples[i] = samples[i + 1] = output[position];
            if (++position != 480) continue;
            Frame(); position = 0;
        }
    }
    private void Frame()
    {
        fixed (float* capture = input, render = reference, result = output)
        {
            if (echo)
            {
                Check(NativeApm.Reverse(apm, &render, stream, stream, &render));
                // Host aligns both streams by WASAPI timestamps. AEC3 estimates
                // the remaining physical speaker-to-microphone delay itself.
                NativeApm.Delay(apm, 0);
            }
            Check(NativeApm.Process(apm, &capture, stream, stream, &result));
        }
    }
    private static void Check(int error) { if (error != 0) throw new InvalidOperationException($"voice processing error {error}"); }
    public void Reset() { position = 0; Array.Clear(input); Array.Clear(reference); Array.Clear(output); }
    public void Dispose()
    {
        if (apm != 0) { NativeApm.Destroy(apm); apm = 0; }
        if (stream != 0) { NativeApm.StreamDestroy(stream); stream = 0; }
    }
}
