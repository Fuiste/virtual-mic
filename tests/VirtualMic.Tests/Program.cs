using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VirtualMic.Core;

var tests = new (string Name, Action Run)[]
{
    ("monitor renders through the actual byte adapter", () => {
        var queue = new SampleQueue(3);
        var provider = new FloatWaveProvider(queue);
        var bytes = Enumerable.Repeat((byte)0x7f, 40).ToArray();
        Check(provider.Read(bytes, 8, 32) == 32, "empty monitor stopped");
        Check(bytes.Skip(8).All(x => x == 0), "empty monitor was not silent");
        queue.Write([.1f, -.2f, .3f, -.4f], 0, 4);
        provider.Read(bytes, 8, 32);
        Near(BitConverter.ToSingle(bytes, 8), .1f);
        Near(BitConverter.ToSingle(bytes, 12), -.2f);
        Near(BitConverter.ToSingle(bytes, 20), -.4f);
        Check(bytes.Take(8).All(x => x == 0x7f), "byte offset overwritten");
        Check(bytes.Skip(24).All(x => x == 0), "tail was not silent");
    }),
    ("capture bytes reach both output adapters with intact stereo samples", () => {
        var input = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var samples = Enumerable.Range(0, 8000).Select(i => i % 2 == 0 ? .2f : -.1f).ToArray();
        var captured = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, captured, 0, captured.Length);
        input.AddSamples(captured, 0, captured.Length);
        var bus = new MixBus(input.ToSampleProvider()) { Settings = new(MonitorEnabled:true, MonitorMic:true) };
        var cable = new FloatWaveProvider(bus);
        var monitor = new FloatWaveProvider(bus.Monitor);
        var bytes = new byte[captured.Length];
        Check(cable.Read(bytes, 0, bytes.Length) == bytes.Length, "cable stopped");
        Near(BitConverter.ToSingle(bytes, bytes.Length - 8), .16f);
        Near(BitConverter.ToSingle(bytes, bytes.Length - 4), -.08f);
        monitor.Read(bytes, 0, bytes.Length);
        Near(BitConverter.ToSingle(bytes, bytes.Length - 8), .08f, .0001f);
        Near(BitConverter.ToSingle(bytes, bytes.Length - 4), -.04f, .0001f);
        monitor.Read(bytes, 0, bytes.Length);
        Check(bytes.All(x => x == 0), "monitor underflow replayed audio");
    }),
    ("microphone passthrough and stereo integrity", () => {
        var bus = new MixBus(new Constant(.2f, -.1f));
        var output = new float[2048]; bus.Read(output, 0, output.Length);
        Near(output[2000], .16f); Near(output[2001], -.08f);
    }),
    ("simultaneous pads mix; retrigger replaces one voice", () => {
        var bus = new MixBus(new Constant(0, 0));
        bus.Play("one", Enumerable.Repeat(.1f, 20000).ToArray());
        bus.Play("two", Enumerable.Repeat(.2f, 20000).ToArray());
        bus.Play("one", Enumerable.Repeat(.1f, 20000).ToArray());
        Check(bus.PlayingIds.Length == 2, "duplicate voice stacked");
        var output = new float[2000]; bus.Read(output, 0, output.Length);
        Near(output[1000], .3f * .7f * .8f);
    }),
    ("monitor is a separate cursor and sounds-only by default", () => {
        var bus = new MixBus(new Constant(.2f, .2f)) { Settings = new(MonitorEnabled:true) };
        bus.Play("one", Enumerable.Repeat(.1f, 20000).ToArray());
        var output = new float[8000]; bus.Read(output, 0, output.Length);
        var monitor = new float[8000]; bus.Monitor.Read(monitor, 0, monitor.Length);
        Near(output[7998], (.2f + .07f) * .8f);
        Near(monitor[7998], .07f * .8f * .5f, .0001f);
    }),
    ("monitor disable flushes old samples", () => {
        var bus = new MixBus(new Constant(.2f, .2f)) { Settings = new(MonitorEnabled:true, MonitorMic:true) };
        var output = new float[4000]; bus.Read(output, 0, output.Length);
        Check(bus.Monitor.BufferedSamples > 0, "nothing monitored");
        bus.Settings = bus.Settings with { MonitorEnabled = false }; bus.Read(output, 0, output.Length);
        Check(bus.Monitor.BufferedSamples == 0, "stale monitor audio survived toggle");
    }),
    ("mute silences the mic while clips continue", () => {
        var bus = new MixBus(new Constant(.5f, .5f)) { Settings = new(MicMuted:true) };
        bus.Play("one", Enumerable.Repeat(.1f, 20000).ToArray());
        var output = new float[16000]; bus.Read(output, 0, output.Length);
        Near(output[15998], .1f * .7f * .8f, .0001f);
    }),
    ("stop all preserves voice passthrough", () => {
        var bus = new MixBus(new Constant(.2f, .2f));
        bus.Play("one", Enumerable.Repeat(.5f, 1000).ToArray()); bus.StopSounds();
        var output = new float[1000]; bus.Read(output, 0, output.Length);
        Check(bus.PlayingIds.Length == 0, "sounds still playing"); Near(output[500], .16f);
    }),
    ("peak guard contains overload and invalid samples", () => {
        var bus = new MixBus(new Constant(.9f, .9f));
        bus.Play("one", Enumerable.Repeat(8f, 4000).ToArray());
        var output = new float[4000]; bus.Read(output, 0, output.Length);
        Check(output.All(v => float.IsFinite(v) && Math.Abs(v) <= .98f), "unsafe peak");
        Check(bus.Levels.Limited, "overload not reported");
        bus.Play("bad", [float.NaN, float.PositiveInfinity]); bus.Read(output, 0, output.Length);
        Check(output.All(float.IsFinite), "non-finite audio");
    }),
    ("effects leave soundboard clips untouched", () => {
        var plain = new MixBus(new Constant(0, 0));
        var effected = new MixBus(new Constant(0, 0)) { Settings = new(BassEnabled:true, BassDb:18, DistortionEnabled:true, Drive:20, DistortionMix:1) };
        var data = Enumerable.Range(0, 2000).Select(i => .2f * MathF.Sin(i * .1f)).ToArray();
        plain.Play("one", data); effected.Play("one", data);
        var a = new float[2000]; var b = new float[2000]; plain.Read(a, 0, a.Length); effected.Read(b, 0, b.Length);
        Check(a.SequenceEqual(b), "voice effects leaked into clip bus");
    }),
    ("bass shelf increases low frequencies", () => {
        var fx = new VoiceEffects(); double dryPower = 0, wetPower = 0;
        for (int block = 0; block < 150; block++) {
            var samples = new float[960];
            for (int i = 0; i < samples.Length; i += 2) samples[i] = samples[i+1] = .01f * MathF.Sin(2 * MathF.PI * 60 * (block*480+i/2)/48000);
            if (block > 100) dryPower += samples.Sum(x => (double)x*x);
            fx.Process(samples, samples.Length, new(BassEnabled:true, BassDb:12));
            if (block > 100) wetPower += samples.Sum(x => (double)x*x);
        }
        Check(wetPower / dryPower > 8, "bass did not increase low-frequency energy");
    }),
    ("distortion changes the mic waveform", () => {
        var fx = new VoiceEffects(); var samples = Enumerable.Repeat(.1f, 16000).ToArray();
        fx.Process(samples, samples.Length, new(DistortionEnabled:true, Drive:10, DistortionMix:1));
        Check(samples[^1] > .7f && samples[^1] < .8f, "distortion failed");
    }),
    ("queue overflow retains latest frames and underflow is silence", () => {
        var queue = new SampleQueue(3); queue.Write([1,2,3,4,5,6,7,8], 0, 8);
        var output = new float[8]; queue.Read(output, 0, 8);
        Check(output.SequenceEqual(new float[]{3,4,5,6,7,8,0,0}), "queue order / overflow wrong");
        Check(queue.DroppedSamples == 2, "drop count wrong");
    }),
    ("queue wraps without reordering stereo frames", () => {
        var queue = new SampleQueue(3); queue.Write([1,2,3,4],0,4);
        var discard = new float[2]; queue.Read(discard,0,2); queue.Write([5,6,7,8],0,4);
        var output = new float[6]; queue.Read(output,0,6);
        Check(output.SequenceEqual(new float[]{3,4,5,6,7,8}), "wrapped queue corrupt");
    }),
    ("clip completion removes voice; offset is honored", () => {
        var bus = new MixBus(new Constant(0,0)); bus.Play("one", [.1f,.1f]);
        var output = Enumerable.Repeat(99f, 12).ToArray(); bus.Read(output, 2, 8);
        Check(output[0] == 99 && output[10] == 99, "offset overwrite");
        Check(bus.PlayingIds.Length == 0 && output[4] == 0, "clip tail wrong");
    }),
    ("library round-trip, backup, and traversal guard", () => {
        string directory = Path.Combine(Path.GetTempPath(), "virtualmic-test-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new LibraryStore(directory);
            var state = new LibraryState { Pads = [new("1","test","sound.wav",1.2)], Audio = new(BassEnabled:true) };
            store.Save(state); store.Save(state with { MicrophoneId = "test-device" });
            Check(store.Load().Pads[0].Name == "test" && store.Load().Audio.BassEnabled, "roundtrip failed");
            Check(File.Exists(store.StatePath + ".bak"), "backup missing");
            bool refused = false; try { store.Resolve(new("x","x","../escape.wav",1)); } catch (InvalidDataException) { refused = true; }
            Check(refused, "path traversal accepted");
            File.WriteAllText(store.StatePath,"{invalid");
            bool corrupt = false; try { store.Load(); } catch (System.Text.Json.JsonException) { corrupt = true; }
            Check(corrupt && File.ReadAllText(store.StatePath) == "{invalid", "corrupt library overwritten");
        } finally { Directory.Delete(directory, true); }
    }),
    ("concurrent pad edits do not corrupt the mixer", () => {
        var bus = new MixBus(new Constant(.1f,.1f));
        var producer = Task.Run(() => { for (int i=0;i<500;i++) { bus.Play((i%4).ToString(),new float[1000]); if (i%10==0) bus.StopSounds(); } });
        var output = new float[960];
        for (int i=0;i<500;i++) { bus.Read(output,0,output.Length); Check(output.All(float.IsFinite),"invalid sample"); }
        producer.GetAwaiter().GetResult();
    })
};
int failures = 0;
var allTests = tests.Concat(EffectTests.Cases()).ToList();
if (args.Length == 1) allTests.AddRange(VirtualMic.Diagnostics.PluginSmoke.Cases(args[0]));
allTests.Add(("invalid manifests preserve built-ins", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "virtualmic-plugin-test-" + Guid.NewGuid().ToString("N"));
    try { VirtualMic.Diagnostics.PluginSmoke.InvalidManifests(directory); }
    finally { Directory.Delete(directory, true); }
}));
foreach (var test in allTests)
{
    try { test.Run(); Console.WriteLine($"pass / {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL / {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{allTests.Count - failures}/{allTests.Count} passed");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Near(float actual, float expected, float tolerance = .00001f) => Check(Math.Abs(actual - expected) < tolerance, $"expected {expected}, got {actual}");
sealed class Constant(float left, float right) : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
    public int Read(float[] buffer,int offset,int count) { for(int i=0;i<count;i++) buffer[offset+i] = i%2 == 0 ? left : right; return count; }
}
