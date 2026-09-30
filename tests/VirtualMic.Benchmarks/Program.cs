using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;
using VirtualMic.Core;
using VirtualMic.Core.Effects;

if (args.Length < 2) throw new ArgumentException("usage: benchmarks <plugins> <report.json> [--golden]");
bool golden = args.Contains("--golden");
using var catalog = new EffectCatalog(); catalog.LoadDirectory(Path.GetFullPath(args[0]));
if (catalog.Errors.Count > 0 || catalog.Effects.Count() != 6) throw new Exception(string.Join("; ", catalog.Errors));
var input = new Signal();
var rows = new List<object>();
string[][] chains = [[], ["virtualmic.bass"], ["virtualmic.distortion"], ["example.delay"],
    ["virtualmic.podcast"], ["virtualmic.echo-cancellation", "virtualmic.noise-suppression", "virtualmic.podcast"],
    ["virtualmic.echo-cancellation", "virtualmic.noise-suppression", "virtualmic.podcast", "virtualmic.bass", "virtualmic.distortion", "example.delay"]];
string[] names = ["dry", "bass", "distortion", "delay", "podcast", "clean voice", "all six + 8 pads + monitor"];
for (int scenario = 0; scenario < chains.Length; scenario++)
{
    int passes = golden ? 1 : 3;
    for (int pass = 0; pass < passes; pass++)
    {
        input.Position = 0;
        var slots = chains[scenario].Select((id, i) => new EffectSlot { InstanceId = "bench-" + i, EffectId = id,
            Target = scenario == 6 ? EffectTarget.Both : EffectTarget.Mic, Enabled = true }).ToArray();
        using var bus = new MixBus(input, catalog) { Settings = new(MonitorEnabled: scenario == 6, Effects: slots) };
        if (scenario == 6)
            for (int i = 0; i < 8; i++) bus.Play("pad-" + i, input.Clip, .1f + .01f * i);
        var output = new float[960]; var monitor = new float[960];
        const int blocks = 2000;
        var timings = new double[blocks];
        // Warm tiered compilation and native state; fixture stays longer than the run.
        for (int i = 0; i < 500; i++) { bus.Read(output, 0, output.Length); if (scenario == 6) bus.Monitor.Read(monitor, 0, monitor.Length); }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        double cpuStart = process.TotalProcessorTime.TotalMilliseconds;
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < blocks; i++)
        {
            if (golden && i is 100 or 300 or 500 or 700)
            {
                if (i == 100) bus.Settings = bus.Settings with { MasterGain = .6f, MicGain = .7f, SoundGain = .4f, MonitorMic = true };
                if (i == 300) bus.Settings = bus.Settings with { MicMuted = true, MonitorEnabled = false };
                if (i == 500) bus.Settings = bus.Settings with { MicMuted = false, MonitorEnabled = scenario == 6 };
                if (i == 700)
                    bus.Settings = bus.Settings with { Effects = slots.Select(s => s with { Parameters = catalog.Find(s.EffectId)!.Definition.Parameters.ToDictionary(p => p.Id, p => p.Minimum + (p.Maximum - p.Minimum) * .8f) }).ToArray() };
            }
            long blockStart = Stopwatch.GetTimestamp();
            bus.Read(output, 0, output.Length);
            if (scenario == 6) bus.Monitor.Read(monitor, 0, monitor.Length);
            timings[i] = Stopwatch.GetElapsedTime(blockStart).TotalMicroseconds;
            if (golden) { hash.AppendData(MemoryMarshal.AsBytes(output.AsSpan())); if (scenario == 6) hash.AppendData(MemoryMarshal.AsBytes(monitor.AsSpan())); }
        }
        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
        double processCpuMs = process.TotalProcessorTime.TotalMilliseconds - cpuStart;
        Array.Sort(timings);
        string? audioHash = golden ? Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() : null;
        rows.Add(new { scenario = names[scenario], pass, elapsedMs, audioSeconds = blocks * .01,
            processCpuMs, oneCoreCpuPercent = processCpuMs / (blocks * 10) * 100, p50Microseconds = timings[blocks / 2],
            p99Microseconds = timings[(int)(blocks * .99)], allocatedBytes = allocated, audioHash });
        Console.WriteLine($"{names[scenario],-32} {elapsedMs,8:0.0} ms / {blocks * .01:0}s audio; {allocated} bytes; p99 {timings[(int)(blocks * .99)]:0.0} us" + (golden ? " / " + audioHash : ""));
    }
}
// Include the timestamp alignment work done alongside DSP in the capture/render paths.
{
    var mic = new CaptureTimeline(); var speaker = new CaptureTimeline();
    float[] packet = input.Clip[..960], output = new float[960], reference = new float[960]; double[] times = new double[480];
    const int blocks = 5000;
    long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
    for (int i = 0; i < blocks; i++)
    {
        double t = 10000000 + i * 480 * CaptureClock.NominalPeriod;
        mic.Write(packet, t); speaker.Write(packet, t);
        mic.ReadMicrophone(output, times); speaker.ReadReference(times, reference);
    }
    double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    rows.Add(new { scenario = "capture + reference alignment", elapsedMs, audioSeconds = blocks * .01,
        oneCorePercent = elapsedMs / (blocks * 10) * 100, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation });
    Console.WriteLine($"capture + reference alignment    {elapsedMs:0.0} ms / {blocks * .01:0}s audio");
}
var report = new { runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    logicalProcessors = Environment.ProcessorCount, core = typeof(MixBus).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    blockFrames = 480, sampleRate = 48000, synthetic = true, audioDevicesOpened = false, golden, rows };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

sealed class Signal : ISampleProvider, ISpeakerReferenceSource
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public int Position;
    public float[] Clip { get; } = Enumerable.Range(0, 96000 * 40).Select(i => .025f * MathF.Sin(i / 2 * (2 * MathF.PI * 173 / 48000)) + .009f * MathF.Sin(i / 2 * .311f)).ToArray();
    public int Read(float[] buffer, int offset, int count)
    {
        Clip.AsSpan(Position, count).CopyTo(buffer.AsSpan(offset, count)); Position += count;
        if (Position + count > Clip.Length) Position = 0;
        return count;
    }
    public bool CopyReference(Span<float> destination, out bool discontinuity)
    {
        discontinuity = false;
        for (int i = 0; i < destination.Length; i++) destination[i] = .02f * MathF.Sin((Position + i) / 2 * (2 * MathF.PI * 417 / 48000));
        return true;
    }
}
