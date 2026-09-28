using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VirtualMic.App.Audio;
using VirtualMic.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--run")
        {
            Console.WriteLine("opt-in hardware test: pass --run. uses saved devices; plays a quiet tone; mic muted; no audio saved.");
            return 2;
        }
        int result = 1;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(async () =>
        {
            try { await Run(); result = 0; }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
        });
        Dispatcher.Run();
        return result;
    }

    private static async Task Run()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualMic", "library.json");
        var saved = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(path))!;
        var inputs = AudioEngine.Devices(DataFlow.Capture);
        var outputs = AudioEngine.Devices(DataFlow.Render);
        var mic = inputs.Single(x => x.Id == saved.MicrophoneId);
        var cable = outputs.Single(x => x.Id == saved.OutputId);
        var headphones = outputs.Single(x => x.Id == saved.MonitorId);
        Check(cable.Name.StartsWith("CABLE Input (", StringComparison.OrdinalIgnoreCase), "this test requires the standard vb-cable pair");
        var receiver = inputs.Single(x => x.Name.StartsWith("CABLE Output (", StringComparison.OrdinalIgnoreCase));
        using var enumerator = new MMDeviceEnumerator();
        using var cableDevice = enumerator.GetDevice(receiver.Id);
        using var headphonesDevice = enumerator.GetDevice(headphones.Id);
        using var received = new WasapiCapture(cableDevice);
        using var heard = new WasapiLoopbackCapture(headphonesDevice);
        var cableMeter = new ToneMeter(received.WaveFormat);
        var monitorMeter = new ToneMeter(heard.WaveFormat);
        received.DataAvailable += (_, e) => cableMeter.Add(e.Buffer, e.BytesRecorded);
        heard.DataAvailable += (_, e) => monitorMeter.Add(e.Buffer, e.BytesRecorded);
        var faults = new List<string>();
        var monitorFaults = new List<string>();
        received.RecordingStopped += (_, e) => { if (e.Exception is not null) faults.Add(e.Exception.ToString()); };
        heard.RecordingStopped += (_, e) => { if (e.Exception is not null) faults.Add(e.Exception.ToString()); };
        using var engine = new AudioEngine();
        engine.Faulted += faults.Add;
        engine.MonitorFaulted += monitorFaults.Add;
        var settings = new AudioSettings(MicMuted:true, MonitorEnabled:false, MonitorGain:.5f);
        received.StartRecording(); heard.StartRecording();
        engine.Start(mic, cable, headphones, settings);
        await Task.Delay(600);
        Check(engine.IsRunning && faults.Count == 0, "mic/cable start failed");
        settings = settings with { MonitorEnabled = true };
        engine.UpdateSettings(settings, headphones);
        await Task.Delay(400);
        var tone = new float[48000 * 2 * 2];
        for (int frame = 0; frame < tone.Length / 2; frame++)
        {
            float fade = Math.Min(1, Math.Min(frame / 480f, (tone.Length / 2 - 1 - frame) / 480f));
            float sample = .02f * fade * MathF.Sin(2 * MathF.PI * 997 * frame / 48000);
            tone[frame * 2] = tone[frame * 2 + 1] = sample;
        }
        engine.Bus!.Play("smoke-tone", tone);
        await Task.Delay(2600);
        Check(faults.Count == 0 && monitorFaults.Count == 0, string.Join("; ", faults.Concat(monitorFaults)));
        Check(cableMeter.Amplitude > .003, "997 hz test tone missing from cable output");
        Check(monitorMeter.Amplitude > .001, "997 hz test tone missing from headphone loopback");
        engine.UpdateSettings(settings with { MonitorEnabled = false }, headphones);
        await Task.Delay(200);
        engine.UpdateSettings(settings, headphones);
        await Task.Delay(300);
        Check(engine.IsRunning && monitorFaults.Count == 0, "listen toggle failed");
        engine.StopMonitor();
        engine.UpdateSettings(settings, new AudioDevice("deliberately-missing-smoke-device", "missing headphones", false));
        Check(monitorFaults.Count == 1 && engine.IsRunning && faults.Count == 0, "monitor failure took down the mic route");
        engine.UpdateSettings(settings, headphones);
        await Task.Delay(300);
        engine.Stop();
        await Task.Delay(200);
        engine.Start(mic, cable, headphones, settings);
        await Task.Delay(1000);
        Check(engine.IsRunning && faults.Count == 0 && monitorFaults.Count == 1, "restart failed");
        engine.Stop(); received.StopRecording(); heard.StopRecording();
        await Task.Delay(200);
        Check(faults.Count == 0, string.Join("; ", faults));
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, cableToneAmplitude = cableMeter.Amplitude, monitorToneAmplitude = monitorMeter.Amplitude,
            monitorFailureIsolated = true, restartPassed = true, microphoneMuted = true, audioSaved = false }));
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // In-memory level analysis only; no microphone or loopback recordings are kept.
    private sealed class ToneMeter(WaveFormat format)
    {
        private readonly object gate = new();
        private double amplitude;
        public double Amplitude { get { lock (gate) return amplitude; } }
        public void Add(byte[] buffer, int count)
        {
            if (format.BitsPerSample != 32) throw new NotSupportedException("smoke meter requires float32 capture");
            int frames = count / format.BlockAlign;
            if (frames < 480) return;
            double sin = 0, cos = 0;
            for (int i = 0; i < frames; i++)
            {
                float sample = BitConverter.ToSingle(buffer, i * format.BlockAlign);
                double phase = 2 * Math.PI * 997 * i / format.SampleRate;
                sin += sample * Math.Sin(phase); cos += sample * Math.Cos(phase);
            }
            lock (gate) amplitude = Math.Max(amplitude, 2 * Math.Sqrt(sin * sin + cos * cos) / frames);
        }
    }
}
