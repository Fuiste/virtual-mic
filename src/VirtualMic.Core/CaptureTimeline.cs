namespace VirtualMic.Core;

public interface ISpeakerReferenceSource
{
    // Called immediately after the matching microphone Read, on the render thread.
    bool CopyReference(Span<float> destination, out bool discontinuity);
}

// Packet timestamps are WASAPI QPC positions in 100 ns units. Holding 20 ms of
// microphone audio lets the independent loopback callback catch up. Reference
// lookup interpolates by time, accommodating different device clocks; missing
// packets yield silence instead of replaying stale speaker audio.
public sealed class CaptureTimeline(int capacityFrames = 48000)
{
    private readonly object gate = new();
    private readonly float[] audio = new float[capacityFrames * 2];
    private readonly double[] times = new double[capacityFrames];
    private int head, count, generation;
    public int Generation { get { lock (gate) return generation; } }
    public int BufferedFrames { get { lock (gate) return count; } }
    public void Write(ReadOnlySpan<float> samples, double qpc100ns, bool discontinuity = false, double framePeriod = CaptureClock.NominalPeriod)
    {
        if (samples.Length % 2 != 0 || !double.IsFinite(qpc100ns) || qpc100ns <= 0 || !double.IsFinite(framePeriod) || framePeriod <= 0) throw new ArgumentException("invalid capture packet");
        lock (gate)
        {
            if (discontinuity || (count > 0 && qpc100ns <= times[(head + count - 1) % capacityFrames]))
            { head = count = 0; generation++; }
            bool overflow = false;
            int index = (head + count) % capacityFrames;
            for (int i = 0; i < samples.Length; i += 2)
            {
                if (count == capacityFrames) { if (++head == capacityFrames) head = 0; count--; if (!overflow) generation++; overflow = true; }
                count++;
                audio[index * 2] = samples[i]; audio[index * 2 + 1] = samples[i + 1];
                times[index] = qpc100ns + (i / 2) * framePeriod;
                if (++index == capacityFrames) index = 0;
            }
        }
    }
    public void ReadMicrophone(Span<float> destination, Span<double> timestamps, int holdFrames = 960)
    {
        destination.Clear(); timestamps.Clear();
        lock (gate)
        {
            // Bound latency following a stopped/blocked renderer, preserving stereo frames.
            if (count > 5760) { int skip = count - 2880; head = (head + skip) % capacityFrames; count -= skip; generation++; }
            int frames = Math.Min(destination.Length / 2, Math.Max(0, count - holdFrames));
            int first = Math.Min(frames, capacityFrames - head);
            audio.AsSpan(head * 2, first * 2).CopyTo(destination);
            times.AsSpan(head, first).CopyTo(timestamps);
            audio.AsSpan(0, (frames - first) * 2).CopyTo(destination[(first * 2)..]);
            times.AsSpan(0, frames - first).CopyTo(timestamps[first..]);
            head = (head + frames) % capacityFrames; count -= frames;
        }
    }
    public void ReadReference(ReadOnlySpan<double> timestamps, Span<float> destination)
    {
        destination.Clear();
        lock (gate)
        {
            for (int i = 0; i < timestamps.Length; i++)
            {
                double t = timestamps[i]; if (t <= 0) continue;
                int next = head + 1 == capacityFrames ? 0 : head + 1;
                while (count > 1 && times[next] <= t)
                { head = next; count--; next = head + 1 == capacityFrames ? 0 : head + 1; }
                if (count == 0) continue;
                double delta = t - times[head];
                if (count <= 1) next = head;
                double width = times[next] - times[head];
                // Never bridge packet loss or loopback's silence gaps.
                if (delta < -210 || delta > 1000 || (width > 1000 && delta > 210)) continue;
                float fraction = width > 0 && width <= 1000 ? (float)Math.Clamp(delta / width, 0, 1) : 0;
                destination[i * 2] = audio[head * 2] + fraction * (audio[next * 2] - audio[head * 2]);
                destination[i * 2 + 1] = audio[head * 2 + 1] + fraction * (audio[next * 2 + 1] - audio[head * 2 + 1]);
            }
        }
    }
}
