using System.IO;
using VirtualMic.Core.Effects;
using VirtualMic.PluginApi;

namespace VirtualMic.Diagnostics;

// Shared by the console suite and the published single-file app's explicit test mode.
internal static class PluginSmoke
{
    public static void Run(string directory)
    {
        foreach (var test in Cases(directory)) test.Run();
    }

    public static IEnumerable<(string Name, Action Run)> Cases(string directory) =>
    [
        ("external delay preserves stereo impulse timing across partial blocks", () => WithDelay(directory, Timing)),
        ("delay dry/wet endpoints and blend have the expected amplitudes", () => WithDelay(directory, Mix)),
        ("delay feedback decays correctly across ring-buffer wraps", () => WithDelay(directory, Feedback)),
        ("delay parameter edits retain history and reset clears it", () => WithDelay(directory, EditsAndReset)),
        ("delay processors have independent state", () => WithDelay(directory, IndependentInstances)),
        ("delay stays finite and bounded at parameter extremes", () => WithDelay(directory, Extremes))
    ];

    private static void WithDelay(string directory, Action<EffectRegistration> test)
    {
        using var catalog = new EffectCatalog();
        catalog.LoadDirectory(directory);
        if (catalog.Errors.Count != 0) throw new InvalidDataException(string.Join("; ", catalog.Errors));
        test(catalog.Find("example.delay") ?? throw new InvalidDataException("reference delay plugin did not load"));
    }

    private static IReadOnlyDictionary<string, float> Parameters(EffectRegistration plugin, float time = 30, float feedback = 0, float mix = 100) =>
        plugin.Parameters(new Dictionary<string, float> { ["delay"] = time, ["feedback"] = feedback, ["mix"] = mix });

    // Use real host-sized, uneven chunks, including a single stereo frame. State
    // must cross calls: a test using one huge span would miss block-boundary bugs.
    private static void Render(IAudioEffect effect, float[] samples, IReadOnlyDictionary<string, float> parameters)
    {
        ReadOnlySpan<int> sizes = [2, 258, 2048, 960];
        int block = 0;
        for (int offset = 0; offset < samples.Length; block++)
        {
            int length = Math.Min(sizes[block % sizes.Length], samples.Length - offset);
            effect.Process(samples.AsSpan(offset, length), parameters); offset += length;
        }
    }

    private static void Timing(EffectRegistration plugin)
    {
        foreach (int time in new[] { 30, 240, 1000 })
        {
            using var effect = plugin.Factory.Create(48000, 2);
            int delaySamples = time * 48 * 2;
            float[] signal = new float[delaySamples * 2 + 2]; signal[0] = .25f; signal[1] = -.125f;
            Render(effect, signal, Parameters(plugin, time));
            Near(signal[delaySamples], .25f, "left delayed impulse");
            Near(signal[delaySamples + 1], -.125f, "right delayed impulse");
            for (int i = 0; i < signal.Length; i++)
                if (i != delaySamples && i != delaySamples + 1) Near(signal[i], 0, "unexpected audio outside delayed impulse");
        }
    }

    private static void Mix(EffectRegistration plugin)
    {
        foreach (int mix in new[] { 0, 25, 100 })
        {
            using var effect = plugin.Factory.Create(48000, 2);
            float[] signal = new float[3000]; signal[0] = .4f; signal[1] = -.2f;
            Render(effect, signal, Parameters(plugin, mix: mix));
            float wet = mix / 100f;
            Near(signal[0], .4f * (1 - wet), "initial dry mix");
            Near(signal[1], -.2f * (1 - wet), "initial right dry mix");
            Near(signal[2880], .4f * wet, "delayed wet mix");
            Near(signal[2881], -.2f * wet, "delayed right wet mix");
        }
    }

    private static void Feedback(EffectRegistration plugin)
    {
        foreach (int time in new[] { 30, 1000 })
        {
            using var effect = plugin.Factory.Create(48000, 2);
            int delaySamples = time * 48 * 2;
            float[] signal = new float[delaySamples * 3 + 2]; signal[0] = .4f;
            Render(effect, signal, Parameters(plugin, time, feedback: 50));
            Near(signal[delaySamples], .4f, "first repeat");
            Near(signal[delaySamples * 2], .2f, "second repeat");
            Near(signal[delaySamples * 3], .1f, "third repeat after ring wrap");
            for (int i = 1; i < signal.Length; i += 2) Near(signal[i], 0, "left feedback leaked right");
        }
    }

    private static void EditsAndReset(EffectRegistration plugin)
    {
        using var effect = plugin.Factory.Create(48000, 2);
        Render(effect, [.5f, 0], Parameters(plugin, feedback: 50));
        // Moving the tap keeps the impulse already stored in the ring.
        float[] tail = new float[6000]; Render(effect, tail, Parameters(plugin, time: 60, feedback: 50));
        Near(tail[5758], .5f, "time edit discarded delay history");
        float[] transition = [.4f, 0]; Render(effect, transition, Parameters(plugin, time: 60, mix: 0));
        Near(transition[0], .4f * .002f, "mix edit was not smoothed");
        effect.Reset();
        float[] silence = new float[24000]; Render(effect, silence, Parameters(plugin, time: 60));
        if (silence.Any(v => v != 0)) throw new InvalidDataException("reset left stale delay audio");
        effect.Reset();
        float[] dry = [.4f, 0]; Render(effect, dry, Parameters(plugin, mix: 0));
        Near(dry[0], .4f, "reset did not honor initial parameters");
    }

    private static void IndependentInstances(EffectRegistration plugin)
    {
        using var mic = plugin.Factory.Create(48000, 2);
        using var sounds = plugin.Factory.Create(48000, 2);
        var parameters = Parameters(plugin, feedback: 85);
        Render(mic, [.5f, -.25f], parameters);
        float[] silence = new float[12000]; Render(sounds, silence, parameters);
        if (silence.Any(v => v != 0)) throw new InvalidDataException("plugin shared state between sources or rows");
        Render(mic, silence, parameters); Near(silence[2878], .5f, "independent processor lost history");
    }

    private static void Extremes(EffectRegistration plugin)
    {
        using var effect = plugin.Factory.Create(48000, 2);
        float[] block = new float[2048];
        foreach (int time in new[] { 30, 1000 })
        {
            var parameters = Parameters(plugin, time, feedback: 85);
            for (int n = 0; n < 300; n++)
            {
                Array.Fill(block, .1f); Render(effect, block, parameters);
                if (block.Any(v => !float.IsFinite(v) || Math.Abs(v) > .667f))
                    throw new InvalidDataException("delay unstable at maximum feedback");
            }
        }
    }

    private static void Near(float actual, float expected, string message)
    {
        if (!float.IsFinite(actual) || Math.Abs(actual - expected) > .00001f)
            throw new InvalidDataException($"{message}: expected {expected}, got {actual}");
    }

    public static void InvalidManifests(string directory)
    {
        Directory.CreateDirectory(directory);
        string bad = Path.Combine(directory, "invalid"); Directory.CreateDirectory(bad);
        foreach (var manifest in new[] { "{invalid", "{\"apiVersion\":999,\"assembly\":\"missing.dll\"}", "{\"apiVersion\":1,\"assembly\":\"../escape.dll\"}" })
        {
            File.WriteAllText(Path.Combine(bad, "plugin.json"), manifest);
            using var catalog = new EffectCatalog(); catalog.LoadDirectory(directory);
            if (catalog.Errors.Count != 1 || catalog.Effects.Count() != 2)
                throw new InvalidDataException("bad plugin prevented built-in effects from loading");
        }
    }
}
