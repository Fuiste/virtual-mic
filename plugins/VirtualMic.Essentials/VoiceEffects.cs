namespace VirtualMic.Essentials;

public sealed class VoiceEffects
{
    private readonly Shelf left = new();
    private readonly Shelf right = new();
    private float bass;
    private float wet;
    private float gain = 1;
    private float drive = 1;

    public void Process(Span<float> samples, float targetBass, float targetDrive, float targetWet, float targetGain = 1)
    {
        // Update once per block, approach gradually to reduce parameter clicks.
        float nextBass = bass + (targetBass - bass) * .15f;
        if (Math.Abs(nextBass - bass) > .001f)
        {
            bass = nextBass;
            left.Configure(bass);
            right.Configure(bass);
        }
        for (int i = 0; i < samples.Length; i += 2)
        {
            wet += (targetWet - wet) * .002f;
            gain += (targetGain - gain) * .002f;
            drive += (targetDrive - drive) * .002f;
            float l = left.Transform(Finite(samples[i]));
            float r = right.Transform(Finite(samples[i + 1]));
            // Bass-only processing never needs tanh. For distortion the same
            // normalization applies to both channels; calculate it once per frame.
            if (wet == 0) { samples[i] = l * gain; samples[i + 1] = r * gain; }
            else
            {
                float normalization = MathF.Tanh(drive);
                samples[i] = (l * (1 - wet) + MathF.Tanh(l * drive) / normalization * wet) * gain;
                samples[i + 1] = (r * (1 - wet) + MathF.Tanh(r * drive) / normalization * wet) * gain;
            }
        }
    }

    public void Reset()
    {
        bass = wet = 0; gain = drive = 1;
        left.Reset(); right.Reset();
    }

    public static float Finite(float sample) => float.IsFinite(sample) ? sample : 0;

    // RBJ low-shelf biquad, slope = 1. Preserve delay state during coefficient changes.
    private sealed class Shelf
    {
        private double b0 = 1, b1, b2, a1, a2, z1, z2;
        public void Reset() { b0 = 1; b1 = b2 = a1 = a2 = z1 = z2 = 0; }
        public void Configure(float decibels)
        {
            double a = Math.Pow(10, decibels / 40.0);
            double omega = 2 * Math.PI * 180 / 48000;
            double c = Math.Cos(omega);
            double beta = Math.Sqrt(2 * a) * Math.Sin(omega);
            double a0 = a + 1 + (a - 1) * c + beta;
            b0 = a * (a + 1 - (a - 1) * c + beta) / a0;
            b1 = 2 * a * (a - 1 - (a + 1) * c) / a0;
            b2 = a * (a + 1 - (a - 1) * c - beta) / a0;
            a1 = -2 * (a - 1 + (a + 1) * c) / a0;
            a2 = (a + 1 + (a - 1) * c - beta) / a0;
        }
        public float Transform(float sample)
        {
            double output = sample * b0 + z1;
            z1 = sample * b1 - a1 * output + z2;
            z2 = sample * b2 - a2 * output;
            return (float)output;
        }
    }
}
