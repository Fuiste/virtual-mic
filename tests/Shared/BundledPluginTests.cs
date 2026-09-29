using VirtualMic.Core;
using VirtualMic.Core.Effects;
using VirtualMic.PluginApi;

namespace VirtualMic.Diagnostics;

internal static class BundledPluginTests
{
    public static IEnumerable<(string Name, Action Run)> Cases(string directory)
    {
        yield return ("all six bundled effects load from disk with no host fallback", () =>
        {
            using var empty = new EffectCatalog(); Check(!empty.Effects.Any(), "host still registers built-ins");
            using var catalog = Load(directory); Check(catalog.Effects.Count() == 6, "bundled plugin count differs");
            foreach (var item in catalog.Effects)
            {
                using var effect = item.Factory.Create(48000, 2);
                var samples = new float[960];
                if (item.Definition.RequiresSpeakerReference) ((IReferenceAudioEffect)effect).Process(samples, new float[960], item.Parameters(new Dictionary<string, float>()));
                else effect.Process(samples, item.Parameters(new Dictionary<string, float>()));
                Check(samples.All(float.IsFinite), "invalid silence");
            }
        });
        yield return ("podcast voice reduces dynamic range and limits overloads", () =>
        {
            using var catalog = Load(directory); var plugin = catalog.Find("virtualmic.podcast")!;
            double Render(float level)
            {
                using var fx = plugin.Factory.Create(48000, 2); var parameters = plugin.Parameters(new Dictionary<string, float>());
                var signal = new float[960]; double power = 0;
                for (int block = 0; block < 250; block++)
                {
                    for (int i = 0; i < 480; i++) signal[i * 2] = signal[i * 2 + 1] = level * MathF.Sin(2 * MathF.PI * 220 * (block * 480 + i) / 48000);
                    fx.Process(signal, parameters);
                    Check(signal.All(x => float.IsFinite(x) && Math.Abs(x) <= .98f), "compressor overload");
                    if (block > 200) power += signal.Sum(x => (double)x * x);
                }
                return Math.Sqrt(power / (49 * 960));
            }
            double quiet = Render(.03f), loud = Render(.3f); Render(3f);
            Check(loud / quiet < 5 && loud / quiet > 1.1, "compressor did not reduce a 20 dB level difference");
            Check(quiet > .018, "quiet voice was crushed");
        });
        yield return ("noise suppression attenuates steady noise without a hard speech gate", () =>
        {
            using var catalog = Load(directory); var plugin = catalog.Find("virtualmic.noise-suppression")!;
            using var fx = plugin.Factory.Create(48000, 2); var p = plugin.Parameters(new Dictionary<string, float>());
            var random = new Random(178); var signal = new float[960]; double before = 0, after = 0, voice = 0;
            for (int block = 0; block < 900; block++)
            {
                for (int i = 0; i < 480; i++)
                {
                    float v = .02f * (float)(random.NextDouble() * 2 - 1);
                    if (block >= 600) v += .12f * MathF.Sin(2 * MathF.PI * 173 * (block * 480 + i) / 48000);
                    signal[i * 2] = signal[i * 2 + 1] = v;
                }
                if (block is >= 300 and < 600) before += signal.Sum(x => (double)x * x);
                fx.Process(signal, p);
                if (block is >= 300 and < 600) after += signal.Sum(x => (double)x * x);
                if (block >= 650) voice += signal.Sum(x => (double)x * x);
            }
            double attenuation = 10 * Math.Log10(before / after);
            Console.WriteLine($"  noise-only attenuation: {attenuation:0.0} db");
            Check(attenuation > 8, "noise reduction below 8 dB");
            Check(voice > after * 5, "voiced input was gated away");
        });
        yield return ("echo cancellation learns delayed speaker audio and preserves an independent voiced tone", () =>
        {
            using var catalog = Load(directory); var plugin = catalog.Find("virtualmic.echo-cancellation")!;
            using var fx = plugin.Factory.Create(48000, 2); var p = plugin.Parameters(new Dictionary<string, float>());
            var random = new Random(42); var far = new float[48000 * 18]; float filtered = 0;
            for (int i = 0; i < far.Length; i++) { filtered = .75f * filtered + .25f * (float)(random.NextDouble() * 2 - 1); far[i] = filtered * .5f; }
            var mic = new float[960]; var reference = new float[960]; double before = 0, after = 0, nearEnergy = 0;
            for (int block = 0; block < 1800; block++)
            {
                for (int i = 0; i < 480; i++)
                {
                    int t = block * 480 + i;
                    reference[i * 2] = reference[i * 2 + 1] = far[t];
                    float echo = t >= 2400 ? .6f * far[t - 2400] : 0; // 50 ms acoustic delay
                    float near = block >= 1400 ? .12f * MathF.Sin(2 * MathF.PI * 173 * t / 48000) : 0;
                    mic[i * 2] = mic[i * 2 + 1] = echo + near;
                }
                if (block is >= 800 and < 1300) before += mic.Sum(x => (double)x * x);
                ((IReferenceAudioEffect)fx).Process(mic, reference, p);
                if (block is >= 800 and < 1300) after += mic.Sum(x => (double)x * x);
                if (block >= 1500) nearEnergy += mic.Sum(x => (double)x * x);
                Check(mic.All(float.IsFinite), "echo canceller invalid output");
            }
            double attenuation = 10 * Math.Log10(before / Math.Max(after, 1e-20));
            Console.WriteLine($"  echo-only attenuation: {attenuation:0.0} db; double-talk rms: {Math.Sqrt(nearEnergy / (300 * 960)):0.000}");
            Check(attenuation > 15, "echo cancellation below 15 dB");
            Check(Math.Sqrt(nearEnergy / (300 * 960)) > .035, "independent voiced tone lost during double talk");
        });
        yield return ("mic-only plugins cannot process pads even with a saved both target", () =>
        {
            using var catalog = Load(directory); using var chain = new EffectChain(catalog);
            chain.Configure([new() { EffectId = "virtualmic.noise-suppression", Enabled = true, Target = EffectTarget.Both }]);
            var pads = Enumerable.Range(0, 3000).Select(i => .3f * MathF.Sin(i * .21f)).ToArray(); var expected = pads.ToArray();
            chain.Process(new float[3000], pads, pads.Length);
            Check(pads.SequenceEqual(expected), "voice cleanup touched soundboard samples");
        });
        yield return ("missing speaker reference bypasses echo cancellation and keeps mic audible", () =>
        {
            using var catalog = Load(directory); using var chain = new EffectChain(catalog);
            chain.Configure([new() { EffectId = "virtualmic.echo-cancellation", Enabled = true }]);
            float[] mic = [.1f, -.2f]; chain.Process(mic, new float[2], 2);
            Check(mic.SequenceEqual(new[] { .1f, -.2f }), "missing reference muted microphone");
        });
        yield return ("native frame adapters handle arbitrary blocks without managed allocation", () =>
        {
            using var catalog = Load(directory);
            foreach (string id in new[] { "virtualmic.noise-suppression", "virtualmic.echo-cancellation" })
            {
                var plugin = catalog.Find(id)!; using var fx = plugin.Factory.Create(48000, 2);
                var p = plugin.Parameters(new Dictionary<string, float>()); var samples = new float[2048]; var reference = new float[2048];
                void Block(int count) { if (plugin.Definition.RequiresSpeakerReference) ((IReferenceAudioEffect)fx).Process(samples.AsSpan(0, count), reference.AsSpan(0, count), p); else fx.Process(samples.AsSpan(0, count), p); }
                for (int i = 0; i < 100; i++) Block(2048);
                long start = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { Block(2); Block(258); Block(2048); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                Check(allocated == 0, $"native frame adapter allocated {allocated} managed bytes");
                fx.Reset(); Block(2); Check(samples[0] == 0 && samples[1] == 0, "reset replayed pending output");
            }
        });
    }
    private static EffectCatalog Load(string directory)
    {
        var result = new EffectCatalog(); result.LoadDirectory(directory);
        if (result.Errors.Count > 0) { string error = string.Join("; ", result.Errors); result.Dispose(); throw new InvalidOperationException(error); }
        return result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
