using System.Runtime.InteropServices;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VirtualMic.Core;

namespace VirtualMic.App.Audio;

// Shared-mode float capture with hardware timestamps. The render callback never
// touches COM. No recording/file output; loopback stays inside this process.
internal sealed class TimedCapture : IDisposable
{
    private readonly MMDevice device;
    private readonly AudioClient client;
    private readonly AudioCaptureClient reader;
    private readonly Thread worker;
    private readonly ManualResetEvent stop = new(false);
    private readonly float[] packet;
    private readonly CaptureClock clock = new();
    private volatile bool available;
    private volatile bool reliableTimestamps = true;
    public bool Available => available;
    public bool ReliableTimestamps => reliableTimestamps;
    public long CapturedFrames;
    public int Discontinuities;
    public CaptureTimeline Timeline { get; } = new();

    public TimedCapture(string deviceId, bool loopback, Action<Exception> failed, Action? timingUnavailable = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDevice(deviceId);
        try
        {
            client = device.AudioClient;
            var flags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
            if (loopback) flags |= AudioClientStreamFlags.Loopback;
            client.Initialize(AudioClientShareMode.Shared, flags, 200000, 0,
                WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), Guid.Empty);
            reader = client.AudioCaptureClient;
            packet = new float[client.BufferSize * 2];
            client.Start(); available = true;
        }
        catch { reader?.Dispose(); client?.Dispose(); device.Dispose(); stop.Dispose(); throw; }
        worker = new Thread(() =>
        {
            try
            {
                while (!stop.WaitOne(5))
                    while (reader.GetNextPacketSize() > 0 && !stop.WaitOne(0))
                    {
                        nint data = reader.GetBuffer(out int frames, out var flags, out _, out long qpc);
                        try
                        {
                            bool invalidTime = (flags & AudioClientBufferFlags.TimestampError) != 0 || qpc <= 0;
                            double timestamp = qpc;
                            if (invalidTime)
                            {
                                // Preserve ordinary mic passthrough on devices with
                                // broken timestamps; only echo cancellation is bypassed.
                                timestamp = Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency) - frames * (10000000.0 / 48000);
                                if (reliableTimestamps) { reliableTimestamps = false; timingUnavailable?.Invoke(); }
                            }
                            int samples = checked(frames * 2);
                            Interlocked.Add(ref CapturedFrames, frames);
                            if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) Interlocked.Increment(ref Discontinuities);
                            if (samples > packet.Length) throw new InvalidOperationException("capture packet exceeds endpoint buffer");
                            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(packet, 0, samples);
                            else Marshal.Copy(data, packet, 0, samples);
                            bool discontinuity = (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
                            var timing = clock.Align(timestamp, frames, discontinuity);
                            Timeline.Write(packet.AsSpan(0, samples), timing.Start, discontinuity, timing.Period);
                        }
                        finally { reader.ReleaseBuffer(frames); }
                    }
            }
            catch (Exception ex) { available = false; if (!stop.WaitOne(0)) failed(ex); }
            finally
            {
                available = false;
                try { client.Stop(); } catch (COMException) { }
                Release(reader.Dispose); Release(client.Dispose); Release(device.Dispose);
                // Dispose may time out on a defective driver; this thread still
                // owns its COM objects until the call returns, avoiding use-after-free.
            }
        }) { IsBackground = true, Name = loopback ? "speaker reference" : "microphone capture" };
        worker.Start();
    }
    private static void Release(Action release) { try { release(); } catch (COMException) { } }
    public void Dispose()
    {
        available = false; stop.Set();
        if (worker.Join(3000)) stop.Dispose();
    }
}

internal sealed class ReferencedMicrophone(TimedCapture microphone) : ISampleProvider, ISpeakerReferenceSource
{
    private double[] timestamps = new double[8192];
    private TimedCapture? speakers;
    private int previousMicGeneration, previousReferenceGeneration;
    private bool changed;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public void SetSpeakers(TimedCapture? capture) => Volatile.Write(ref speakers, capture);
    public int Read(float[] buffer, int offset, int count)
    {
        if (timestamps.Length < count / 2) timestamps = new double[count / 2];
        microphone.Timeline.ReadMicrophone(buffer.AsSpan(offset, count), timestamps.AsSpan(0, count / 2));
        int generation = microphone.Timeline.Generation;
        changed = generation != previousMicGeneration; previousMicGeneration = generation;
        return count;
    }
    public bool CopyReference(Span<float> destination, out bool discontinuity)
    {
        var capture = Volatile.Read(ref speakers);
        discontinuity = changed;
        if (capture is null || !capture.Available || !capture.ReliableTimestamps || !microphone.ReliableTimestamps) return false;
        int generation = capture.Timeline.Generation;
        discontinuity |= generation != previousReferenceGeneration; previousReferenceGeneration = generation;
        capture.Timeline.ReadReference(timestamps.AsSpan(0, destination.Length / 2), destination);
        return true;
    }
}
