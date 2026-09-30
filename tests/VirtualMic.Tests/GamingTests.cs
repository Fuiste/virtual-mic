using System.Text.Json;
using NAudio.Wave;
using VirtualMic.Core;

internal static class GamingTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("24 default hotkeys are distinct and skip f12 / unmodified game keys", () =>
        {
            var keys = Enumerable.Range(0, 24).Select(HotkeyChord.DefaultFor).ToArray();
            Check(keys.Distinct().Count() == 24 && keys.All(k => k.IsValid && k.Key != 0x7b), "invalid/default conflict");
            Check(keys[0].Label == "ctrl+alt+f1" && keys[11].Label == "ctrl+alt+0" && keys[12].Label == "ctrl+alt+shift+f1", "wrong default banks");
            Check(!new HotkeyChord(0x7b, HotkeyModifiers.Control).IsValid && !new HotkeyChord(0x41, HotkeyModifiers.Shift).IsValid, "reserved/unsafe key accepted");
            var gaming = new GamingSettings(); Check(!keys.Contains(gaming.StopSounds) && !keys.Contains(gaming.ToggleOverlay), "action conflict");
        });
        yield return ("old libraries gain defaults once; disabled/custom keys survive saves and pad removal", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "virtualmic-gaming-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var store = new LibraryStore(directory);
                var old = new LibraryState { Version = 2, Pads = [new("a", "a", "a.wav", 1), new("b", "b", "b.wav", 1)] };
                string original = JsonSerializer.Serialize(old); File.WriteAllText(store.StatePath, original);
                var loaded = store.Load(); Check(loaded.Pads[0].Hotkey == HotkeyChord.DefaultFor(0) && loaded.Pads[1].Hotkey == HotkeyChord.DefaultFor(1), "migration didn't assign defaults");
                store.Save(loaded); Check(File.ReadAllText(store.StatePath + ".bak") == original, "original wasn't backed up");
                var custom = new HotkeyChord(0x48, HotkeyModifiers.Control | HotkeyModifiers.Shift);
                store.Save(loaded with { Pads = [loaded.Pads[0] with { Hotkey = null }, loaded.Pads[1] with { Hotkey = custom }], Gaming = new() { HotkeysEnabled = false, OverlayEnabled = false, OverlayCorner = OverlayCorner.BottomLeft } });
                var restored = store.Load(); Check(restored.Version == 3 && restored.Pads[0].Hotkey is null && restored.Pads[1].Hotkey == custom && !restored.Gaming.OverlayEnabled, "bindings/settings did not persist");
                store.Save(restored with { Pads = [restored.Pads[1]] }); Check(store.Load().Pads[0].Hotkey == custom, "removal reassigned another pad's hotkey");
            }
            finally { Directory.Delete(directory, true); }
        });
        yield return ("invalid saved hotkeys preserve original library instead of overwriting", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "virtualmic-bad-hotkey-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var store = new LibraryStore(directory);
                string invalid = JsonSerializer.Serialize(new LibraryState { Pads = [new("x", "x", "x.wav", 1, Hotkey: new(0x7b, HotkeyModifiers.Control))] });
                File.WriteAllText(store.StatePath, invalid);
                try { store.Load(); throw new Exception("invalid hotkey accepted"); } catch (InvalidDataException) { }
                Check(File.ReadAllText(store.StatePath) == invalid, "invalid settings overwritten");
            }
            finally { Directory.Delete(directory, true); }
        });
        yield return ("steady mixer rendering allocates nothing; playback revisions include completion/retrigger/stop", () =>
        {
            using var bus = new MixBus(new Silence()) { Settings = new(Effects: []) };
            float[] output = new float[960];
            for (int i = 0; i < 1000; i++) bus.Read(output, 0, output.Length);
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) bus.Read(output, 0, output.Length);
            Check(GC.GetAllocatedBytesForCurrentThread() == allocation, "render path allocated");
            long revision = bus.PlaybackRevision; bus.Play("pad", new float[2000]); Check(bus.PlaybackRevision > revision, "play wasn't published");
            string?[] ids = new string?[16]; Check(bus.CopyPlayingIds(ids) == 1 && ids[0] == "pad", "playing snapshot incorrect");
            revision = bus.PlaybackRevision; bus.Play("pad", new float[2000]); Check(bus.PlaybackRevision > revision && bus.CopyPlayingIds(ids) == 1, "retrigger stacked/lost revision");
            for (int i = 0; i < 3; i++) bus.Read(output, 0, output.Length);
            Check(bus.CopyPlayingIds(ids) == 0, "completed pad still playing");
            bus.Play("pad", new float[2000]); revision = bus.PlaybackRevision; bus.StopSounds();
            Check(bus.PlaybackRevision > revision && bus.CopyPlayingIds(ids) == 0, "stop wasn't published");
        });
        yield return ("capture timeline bulk copies wrap and overflow without shifting samples or timestamps", () =>
        {
            var timeline = new CaptureTimeline(7); float[] output = new float[8]; double[] times = new double[4];
            timeline.Write([1, -1, 2, -2, 3, -3, 4, -4, 5, -5], 10000000);
            timeline.ReadMicrophone(output, times, 0); timeline.Write([6, -6, 7, -7, 8, -8, 9, -9, 10, -10, 11, -11, 12, -12, 13, -13], 10000000 + 5 * CaptureClock.NominalPeriod);
            timeline.ReadMicrophone(output, times, 0);
            Check(output.SequenceEqual(new float[] { 7, -7, 8, -8, 9, -9, 10, -10 }), "wrap/overflow corrupt");
            Check(Math.Abs(times[0] - (10000000 + 6 * CaptureClock.NominalPeriod)) < .001 && timeline.Generation == 1, "timestamps/generation corrupt");
        });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Silence : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
    }
}
