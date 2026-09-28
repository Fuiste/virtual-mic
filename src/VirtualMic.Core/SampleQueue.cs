using NAudio.Wave;

namespace VirtualMic.Core;

// Bounded, frame-aligned, single-producer/single-consumer monitor queue.
// Drop oldest frames when output clocks drift; never replay stale monitoring.
public sealed class SampleQueue(int capacityFrames = 4800) : ISampleProvider
{
    private readonly object gate = new();
    private readonly float[] samples = new float[capacityFrames * 2];
    private int head;
    private int size;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int BufferedSamples { get { lock (gate) return size; } }
    public long DroppedSamples { get; private set; }

    public void Write(float[] source, int offset, int count)
    {
        if (count % 2 != 0) throw new ArgumentException("stereo frames required", nameof(count));
        lock (gate)
        {
            if (count > samples.Length)
            {
                int skip = count - samples.Length;
                offset += skip;
                count -= skip;
                DroppedSamples += skip;
            }
            int overflow = Math.Max(0, size + count - samples.Length);
            head = (head + overflow) % samples.Length;
            size -= overflow;
            DroppedSamples += overflow;
            int tail = (head + size) % samples.Length;
            int first = Math.Min(count, samples.Length - tail);
            Array.Copy(source, offset, samples, tail, first);
            Array.Copy(source, offset + first, samples, 0, count - first);
            size += count;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            int available = Math.Min(count, size);
            available -= available % 2;
            int first = Math.Min(available, samples.Length - head);
            Array.Copy(samples, head, buffer, offset, first);
            Array.Copy(samples, 0, buffer, offset + first, available - first);
            head = (head + available) % samples.Length;
            size -= available;
            Array.Clear(buffer, offset + available, count - available);
            return count;
        }
    }

    public void Clear() { lock (gate) { head = 0; size = 0; } }
}
