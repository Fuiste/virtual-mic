using System.IO;
using VirtualMic.Core.Effects;

namespace VirtualMic.Diagnostics;

// Shared by the console suite and the published single-file app's explicit test mode.
internal static class PluginSmoke
{
    public static void Run(string directory)
    {
        using var catalog = new EffectCatalog();
        catalog.LoadDirectory(directory);
        if (catalog.Errors.Count != 0) throw new InvalidDataException(string.Join("; ", catalog.Errors));
        var echo = catalog.Find("example.echo") ?? throw new InvalidDataException("example echo plugin did not load");
        using var mic = echo.Factory.Create(48000, 2);
        using var sounds = echo.Factory.Create(48000, 2);
        var parameters = echo.Parameters(new Dictionary<string, float> { ["delay"] = 30, ["feedback"] = 0, ["mix"] = 100 });
        float[] warmup = new float[16000]; mic.Process(warmup, parameters);
        float[] impulse = new float[6000]; impulse[0] = .25f; impulse[1] = -.125f;
        mic.Process(impulse, parameters);
        if (Math.Abs(impulse[2880] - .25f) > .001f || Math.Abs(impulse[2881] + .125f) > .001f)
            throw new InvalidDataException("external echo did not preserve stereo delay");
        float[] silence = new float[6000]; sounds.Process(silence, parameters);
        if (silence.Any(v => v != 0)) throw new InvalidDataException("plugin shared state between processor instances");
        mic.Reset(); Array.Clear(impulse); mic.Process(impulse, parameters);
        if (impulse.Any(v => v != 0)) throw new InvalidDataException("reset left stale echo audio");
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
