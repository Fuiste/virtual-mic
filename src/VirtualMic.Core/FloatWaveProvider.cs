using NAudio.Wave;

namespace VirtualMic.Core;

// Keep a real float array at the sample-provider boundary. NAudio 2's
// SampleToWaveProvider aliases byte[] as float[], which breaks Array.Copy
// and gives Array.Clear byte rather than sample semantics in SampleQueue.
public sealed class FloatWaveProvider(ISampleProvider source) : IWaveProvider
{
    private float[] samples = [];
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count) throw new ArgumentException("buffer is too small");
        if (count % WaveFormat.BlockAlign != 0) throw new ArgumentException("whole audio frames required", nameof(count));
        if (count == 0) return 0;
        int sampleCount = count / sizeof(float);
        if (samples.Length < sampleCount) samples = new float[sampleCount];
        int read = source.Read(samples, 0, sampleCount);
        Buffer.BlockCopy(samples, 0, buffer, offset, read * sizeof(float));
        return read * sizeof(float);
    }
}
