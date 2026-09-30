using VirtualMic.PluginApi;

namespace VirtualMic.Essentials;

public sealed class PodcastPlugin : IAudioEffectPlugin
{
    public EffectDefinition Definition { get; } = new("virtualmic.podcast", "podcast voice",
        [new("amount", "compression", 0, 100, 65, "%", 1), new("gain", "output", -12, 12, 0, "db", .5f)])
        { MicrophoneOnly = true };
    public IAudioEffect Create(int sampleRate, int channels) =>
        sampleRate == 48000 && channels == 2 ? new Compressor() : throw new NotSupportedException("48 kHz stereo required");

    // Linked stereo peak detector, soft knee, 5 ms attack / 140 ms release.
    // Rumble removal and a little makeup gain give useful one-click defaults.
    private sealed class Compressor : IAudioEffect
    {
        private float leftIn, rightIn, leftOut, rightOut, envelope, gain = 1;
        private const float HighPass = .989583f; // 80 Hz, first order
        private static readonly float Attack = MathF.Exp(-1f / (48000 * .005f));
        private static readonly float Release = MathF.Exp(-1f / (48000 * .14f));
        public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
        {
            float amount = parameters["amount"] / 100;
            float threshold = -12 - 14 * amount, ratio = 1 + 4 * amount;
            float makeup = 5 * amount + parameters["gain"];
            float slope = 1 - 1 / ratio;
            float uncompressedGain = MathF.Pow(10, makeup / 20);
            for (int i = 0; i < samples.Length; i += 2)
            {
                float l = HighPass * (leftOut + samples[i] - leftIn);
                float r = HighPass * (rightOut + samples[i + 1] - rightIn);
                leftIn = samples[i]; rightIn = samples[i + 1]; leftOut = l; rightOut = r;
                float peak = Math.Max(Math.Abs(l), Math.Abs(r));
                float speed = peak > envelope ? Attack : Release;
                envelope = peak + speed * (envelope - peak);
                float over = 20 * MathF.Log10(Math.Max(envelope, 1e-9f)) - threshold;
                float reduction = over < -3 ? 0 : over > 3 ? over * slope
                    : (over + 3) * (over + 3) / 12 * slope;
                float target = reduction == 0 ? uncompressedGain : MathF.Pow(10, (makeup - reduction) / 20);
                gain += .004f * (target - gain);
                samples[i] = Limit(l * gain); samples[i + 1] = Limit(r * gain);
            }
        }
        // Transparent below -1.9 dBFS; rounded overloads instead of hard clipping.
        private static float Limit(float x) => Math.Abs(x) <= .8f ? x
            : MathF.CopySign(.8f + .18f * MathF.Tanh((Math.Abs(x) - .8f) / .18f), x);
        public void Reset() { leftIn = rightIn = leftOut = rightOut = envelope = 0; gain = 1; }
        public void Dispose() { }
    }
}
