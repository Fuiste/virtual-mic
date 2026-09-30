using System.Text.Json;
using NAudio.Wave;
using VirtualMic.Core;
using VirtualMic.Core.Effects;
using VirtualMic.PluginApi;

internal static class EffectTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("effect targets route mic, sounds and both independently", () =>
        {
            using var catalog = Catalog();
            foreach (var target in Enum.GetValues<EffectTarget>())
            {
                using var bus = new MixBus(new Input(.1f), catalog) { Settings = new(Effects: [Slot("test.multiply", target)]) };
                bus.Play("pad", Enumerable.Repeat(.2f, 8000).ToArray());
                var output = new float[8000]; bus.Read(output, 0, output.Length);
                float mic = target == EffectTarget.Sounds ? .1f : .2f;
                float sounds = target == EffectTarget.Mic ? .2f : .4f;
                Near(output[^1], (mic + sounds * .7f) * .8f);
            }
        });
        yield return ("reorder and parameter edits preserve processor instances", () =>
        {
            using var catalog = Catalog(); using var chain = new EffectChain(catalog);
            var add = Slot("test.add"); var multiply = Slot("test.multiply");
            chain.Configure([add, multiply]);
            float[] mic = [.1f, .1f]; float[] sounds = [0, 0]; chain.Process(mic, sounds, 2); Near(mic[0], .4f);
            chain.Configure([multiply, add]); mic[0] = mic[1] = .1f; chain.Process(mic, sounds, 2); Near(mic[0], .3f);
            chain.Configure([multiply with { Parameters = new() { ["factor"] = 3 } }, add]);
            mic[0] = mic[1] = .1f; chain.Process(mic, sounds, 2); Near(mic[0], .4f);
            Check(((Factory)catalog.Find("test.multiply")!.Factory).Created == 2, "parameter edit rebuilt processors");
        });
        yield return ("both uses separate delay state; monitor cannot hear hidden mic", () =>
        {
            using var catalog = new EffectCatalog(); catalog.Register(new Factory("test.tail", () => new Tail()));
            using var bus = new MixBus(new Input(.2f), catalog) { Settings = new(MonitorEnabled: true, Effects: [Slot("test.tail", EffectTarget.Both)]) };
            bus.Play("silent", new float[16000]);
            var output = new float[8000]; var monitor = new float[8000];
            for (int i = 0; i < 3; i++) { bus.Read(output, 0, output.Length); bus.Monitor.Read(monitor, 0, monitor.Length); }
            Check(output[^1] > .1f, "microphone tail missing"); Check(monitor.All(v => v == 0), "microphone leaked into sounds-only monitor");
        });
        yield return ("stop sounds clears plugin tails and suppresses generators; mute includes effects", () =>
        {
            using var catalog = new EffectCatalog(); catalog.Register(new Factory("test.generator", () => new Processor((s, _) => s.Fill(.2f))));
            using var bus = new MixBus(new Input(0), catalog) { Settings = new(MonitorEnabled: true, Effects: [Slot("test.generator", EffectTarget.Both)]) };
            var output = new float[8000]; var monitor = new float[8000];
            bus.Play("silent", new float[8000]); bus.Read(output, 0, output.Length); Check(output[^1] > .2f, "generator did not start");
            bus.StopSounds(); bus.Read(output, 0, output.Length); bus.Monitor.Read(monitor, 0, monitor.Length);
            Near(output[^1], .16f); Check(monitor.All(v => v == 0), "stop left a sound effect running");
            bus.Settings = bus.Settings with { MicMuted = true }; bus.Read(output, 0, output.Length); bus.Read(output, 0, output.Length);
            Near(output[^1], 0, .000001f);
            bus.Play("again", new float[8000]); bus.Read(output, 0, output.Length); Near(output[^1], .112f);
        });
        yield return ("faulting or non-finite effects restore input and report only once", () =>
        {
            foreach (bool throws in new[] { true, false })
            {
                using var catalog = new EffectCatalog();
                catalog.Register(new Factory("test.broken", () => new Processor((s, _) =>
                { s.Fill(float.NaN); if (throws) throw new InvalidOperationException("test fault"); })));
                using var chain = new EffectChain(catalog); chain.Configure([Slot("test.broken")]);
                float[] mic = [.1f, -.1f]; chain.Process(mic, new float[2], 2); Near(mic[0], .1f); Near(mic[1], -.1f);
                chain.Process(mic, new float[2], 2); Near(mic[0], .1f);
                Check(chain.TryDequeueFault(out _) && !chain.TryDequeueFault(out _), "fault was missing or repeated");
            }
        });
        yield return ("factory errors and missing plugins bypass without stopping later effects", () =>
        {
            using var catalog = Catalog(); catalog.Register(new Factory("test.badfactory", () => throw new Exception("nope")));
            using var chain = new EffectChain(catalog);
            chain.Configure([Slot("missing.plugin"), Slot("test.badfactory"), Slot("test.multiply")]);
            float[] mic = [.1f, .1f]; chain.Process(mic, new float[2], 2); Near(mic[0], .2f);
            Check(chain.TryDequeueFault(out _) && chain.TryDequeueFault(out _) && !chain.TryDequeueFault(out _), "factory faults not isolated");
        });
        yield return ("bypass and route changes reset tails; removed processors are disposed", () =>
        {
            using var catalog = new EffectCatalog(); var instances = new List<Tail>();
            catalog.Register(new Factory("test.tail", () => { var p = new Tail(); instances.Add(p); return p; }));
            using var chain = new EffectChain(catalog); var slot = Slot("test.tail", EffectTarget.Both); chain.Configure([slot]);
            float[] mic = [.2f, .2f]; float[] sounds = [.1f, .1f]; chain.Process(mic, sounds, 2);
            chain.Configure([slot with { Target = EffectTarget.Mic }]); chain.Process(new float[2], new float[2], 2);
            Check(instances[1].Resets == 1 && instances[0].Resets == 0, "route change reset the wrong branch");
            chain.Configure([slot with { Enabled = false }]); chain.Process(new float[2], new float[2], 2);
            Check(instances[0].Resets == 1, "bypass kept stale state");
            chain.Configure([]); Check(instances.All(p => p.Disposed), "removed processors leaked");
        });
        yield return ("metadata, limits and parameter snapshots reject invalid values", () =>
        {
            using var catalog = Catalog();
            Throws(() => catalog.Register(new Factory("test.multiply", () => new Tail())));
            Throws(() => catalog.Register(new Factory("invalid id", () => new Tail())));
            using var chain = new EffectChain(catalog); var slot = Slot("test.multiply");
            Throws(() => chain.Configure([slot, slot]));
            Throws(() => chain.Configure(Enumerable.Range(0, 17).Select(_ => Slot("test.multiply")).ToArray()));
            var parameters = new Dictionary<string, float> { ["factor"] = float.NaN };
            chain.Configure([slot with { Parameters = parameters }]); parameters["factor"] = 4;
            float[] mic = [.1f, .1f]; chain.Process(mic, new float[2], 2); Near(mic[0], .2f);
        });
        yield return ("legacy settings migrate; empty and missing-plugin chains survive persistence", () =>
        {
            var legacy = JsonSerializer.Deserialize<AudioSettings>("{\"BassEnabled\":true,\"BassDb\":11,\"DistortionMix\":0.65}")!;
            var migrated = EffectDefaults.FromLegacy(legacy);
            Check(migrated[0].Enabled && migrated[0].Target == EffectTarget.Mic && migrated[0].Parameters["gain"] == 11, "legacy bass lost");
            Near(migrated[1].Parameters["mix"], 65);
            var slot = Slot("friends.uninstalled", EffectTarget.Sounds) with { Parameters = new() { ["custom"] = 42 } };
            var restored = JsonSerializer.Deserialize<AudioSettings>(JsonSerializer.Serialize(legacy with { Effects = [slot] }))!;
            Check(restored.Effects![0].Parameters["custom"] == 42 && restored.Effects[0].Target == EffectTarget.Sounds, "unavailable plugin settings lost");
            restored = JsonSerializer.Deserialize<AudioSettings>(JsonSerializer.Serialize(legacy with { Effects = [] }))!;
            Check(restored.Effects is { Length: 0 }, "empty chain became legacy defaults");
        });
        yield return ("live chain edits serialize processing and dispose only unused instances", () =>
        {
            using var catalog = new EffectCatalog(); var processors = new List<Lifetime>();
            catalog.Register(new Factory("test.lifetime", () => { var p = new Lifetime(); processors.Add(p); return p; }));
            using var chain = new EffectChain(catalog);
            var slot = Slot("test.lifetime", EffectTarget.Both); chain.Configure([slot]);
            var render = Task.Run(() => { var mic = new float[960]; var sounds = new float[960]; for (int i = 0; i < 1500; i++) chain.Process(mic, sounds, mic.Length); });
            for (int i = 0; i < 100; i++) chain.Configure(i % 3 == 0 ? [] : [slot with { Enabled = i % 2 == 0 }]);
            render.GetAwaiter().GetResult(); chain.Configure([]);
            Check(processors.All(p => p.Disposed && !p.Violation), "processor lifetime raced render");
        });
        yield return ("library migration versions new saves and rejects malformed chains without overwriting", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "virtualmic-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new LibraryStore(directory);
                string legacy = "{\"Version\":1,\"Pads\":[],\"Audio\":{\"BassEnabled\":true,\"BassDb\":11}}";
                File.WriteAllText(store.StatePath, legacy);
                var restored = store.Load(); Check(restored.Version == 1 && restored.Audio.Effects is null, "old library not accepted");
                store.Save(restored with { Audio = restored.Audio with { Effects = EffectDefaults.FromLegacy(restored.Audio) } });
                Check(store.Load().Version == 3 && File.ReadAllText(store.StatePath + ".bak") == legacy, "migration overwrote rollback data");
                var duplicate = Slot("test.multiply");
                string invalid = JsonSerializer.Serialize(new LibraryState { Audio = new(Effects: [duplicate, duplicate]) });
                File.WriteAllText(store.StatePath, invalid); Throws(() => store.Load());
                Check(File.ReadAllText(store.StatePath) == invalid, "malformed chain was overwritten");
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    private static EffectCatalog Catalog()
    {
        var catalog = new EffectCatalog();
        catalog.Register(new Factory("test.multiply", () => new Processor((s, p) => { for (int i = 0; i < s.Length; i++) s[i] *= p["factor"]; }),
            [new("factor", "factor", 1, 4, 2)]));
        catalog.Register(new Factory("test.add", () => new Processor((s, _) => { for (int i = 0; i < s.Length; i++) s[i] += .1f; })));
        return catalog;
    }
    private static EffectSlot Slot(string id, EffectTarget target = EffectTarget.Mic) => new() { EffectId = id, Enabled = true, Target = target };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float value, float expected, float tolerance = .00001f) => Check(Math.Abs(value - expected) < tolerance, $"expected {expected}, got {value}");
    private static void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("invalid definition accepted"); }
    private delegate void Transform(Span<float> samples, IReadOnlyDictionary<string, float> parameters);
    private sealed class Processor(Transform transform) : IAudioEffect
    { public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters) => transform(samples, parameters); public void Reset() { } public void Dispose() { } }
    private sealed class Factory(string id, Func<IAudioEffect> create, EffectParameter[]? parameters = null) : IAudioEffectPlugin
    {
        public int Created;
        public EffectDefinition Definition { get; } = new(id, id, parameters ?? []);
        public IAudioEffect Create(int sampleRate, int channels) { Created++; return create(); }
    }
    private sealed class Tail : IAudioEffect
    {
        private float previous;
        public int Resets; public bool Disposed;
        public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
        { for (int i = 0; i < samples.Length; i++) { float value = samples[i]; samples[i] = previous; previous = value; } }
        public void Reset() { Resets++; previous = 0; } public void Dispose() => Disposed = true;
    }
    private sealed class Lifetime : IAudioEffect
    {
        private int processing;
        public bool Disposed, Violation;
        public void Process(Span<float> samples, IReadOnlyDictionary<string, float> parameters)
        { Interlocked.Increment(ref processing); if (Disposed) Violation = true; Thread.SpinWait(100); Interlocked.Decrement(ref processing); }
        public void Reset() { if (Disposed || Volatile.Read(ref processing) != 0) Violation = true; }
        public void Dispose() { if (Volatile.Read(ref processing) != 0 || Disposed) Violation = true; Disposed = true; }
    }
    private sealed class Input(float value) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { Array.Fill(buffer, value, offset, count); return count; }
    }
}
