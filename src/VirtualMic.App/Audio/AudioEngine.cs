using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VirtualMic.Core;
using VirtualMic.Core.Effects;

namespace VirtualMic.App.Audio;

public sealed record AudioDevice(string Id, string Name, bool IsVirtual)
{
    public override string ToString() => Name;
}

public sealed class AudioEngine : IDisposable
{
    private WasapiCapture? capture;
    private WasapiOut? output;
    private WasapiOut? monitor;
    private MMDevice? micDevice;
    private MMDevice? outDevice;
    private MMDevice? monitorDevice;
    private volatile bool stopping;
    private int session;
    public MixBus? Bus { get; private set; }
    public bool IsRunning => output?.PlaybackState == PlaybackState.Playing;
    public event Action<string>? Faulted;
    public event Action<string>? MonitorFaulted;

    public static List<AudioDevice> Devices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                string name = device.FriendlyName;
                bool virtualDevice = name.Contains("cable", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("voicemeeter", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("virtual", StringComparison.OrdinalIgnoreCase);
                result.Add(new(device.ID, name, virtualDevice));
            }
        }
        return result.OrderBy(x => x.Name).ToList();
    }

    public void Start(AudioDevice mic, AudioDevice destination, AudioDevice? headphones, AudioSettings settings, EffectCatalog? catalog = null)
    {
        Stop();
        if (mic.IsVirtual) throw new InvalidOperationException("choose a physical microphone to avoid a feedback loop");
        if (!destination.IsVirtual) throw new InvalidOperationException("choose a virtual cable as the output route");
        if (headphones?.Id == destination.Id || headphones?.IsVirtual == true)
            throw new InvalidOperationException("monitoring needs physical headphones or speakers");
        if (settings.MonitorEnabled && headphones is null)
            throw new InvalidOperationException("choose a monitor device first");
        stopping = false;
        int generation = Interlocked.Increment(ref session);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            micDevice = enumerator.GetDevice(mic.Id);
            outDevice = enumerator.GetDevice(destination.Id);
            capture = new WasapiCapture(micDevice);
            var input = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(250),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            capture.DataAvailable += (_, args) =>
            {
                if (stopping) return;
                if (input.BufferedDuration.TotalMilliseconds > 120) input.ClearBuffer();
                input.AddSamples(args.Buffer, 0, args.BytesRecorded);
            };
            capture.RecordingStopped += (_, args) =>
            {
                if (!stopping && generation == Volatile.Read(ref session))
                    Faulted?.Invoke(AudioDiagnostics.Describe("microphone capture stopped", args.Exception));
            };
            ISampleProvider source = input.ToSampleProvider();
            source = Stereo48(source);
            Bus = new MixBus(source, catalog) { Settings = settings };
            output = new WasapiOut(outDevice, AudioClientShareMode.Shared, true, 30);
            output.Init(new FloatWaveProvider(Bus));
            output.PlaybackStopped += (_, args) =>
            {
                if (!stopping && generation == Volatile.Read(ref session))
                    Faulted?.Invoke(AudioDiagnostics.Describe("virtual output stopped", args.Exception));
            };
            capture.StartRecording();
            output.Play();
            UpdateSettings(settings, headphones);
        }
        catch (Exception ex) { AudioDiagnostics.Describe("audio startup failed", ex); Stop(); throw; }
    }

    // Called from the control thread. Monitoring is optional and must never
    // tear down the microphone/cable route when headphones fail.
    public void UpdateSettings(AudioSettings settings, AudioDevice? headphones)
    {
        if (Bus is null) return;
        Bus.Settings = settings;
        if (!settings.MonitorEnabled) { StopMonitor(); return; }
        if (monitor is not null) return;
        try
        {
            if (headphones is null || headphones.IsVirtual)
                throw new InvalidOperationException("choose physical headphones for monitoring");
            using var enumerator = new MMDeviceEnumerator();
            monitorDevice = enumerator.GetDevice(headphones.Id);
            var player = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 40);
            monitor = player;
            player.Init(new FloatWaveProvider(Bus.Monitor));
            int generation = Volatile.Read(ref session);
            player.PlaybackStopped += (_, args) =>
            {
                if (!stopping && generation == Volatile.Read(ref session) && ReferenceEquals(monitor, player))
                    MonitorFaulted?.Invoke(AudioDiagnostics.Describe("headphone monitoring stopped", args.Exception));
            };
            player.Play();
        }
        catch (Exception ex)
        {
            StopMonitor();
            MonitorFaulted?.Invoke(AudioDiagnostics.Describe("headphone monitoring could not start", ex));
        }
    }

    public void StopMonitor()
    {
        var player = monitor;
        monitor = null;
        Release(() => player?.Stop());
        Release(() => player?.Dispose());
        Release(() => monitorDevice?.Dispose()); monitorDevice = null;
        Bus?.Monitor.Clear();
    }

    public static ISampleProvider Stereo48(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.Channels != 2) throw new NotSupportedException("only mono and stereo audio are supported");
        if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
        return source;
    }

    public void Stop()
    {
        stopping = true;
        Interlocked.Increment(ref session);
        // Call only from the control thread, never from an audio callback.
        // Device removal can make one teardown call fail; release the other endpoints anyway.
        Release(() => capture?.StopRecording());
        Release(() => output?.Stop());
        StopMonitor();
        Release(() => capture?.Dispose()); capture = null;
        Release(() => output?.Dispose()); output = null;
        Release(() => micDevice?.Dispose()); micDevice = null;
        Release(() => outDevice?.Dispose()); outDevice = null;
        Bus?.Dispose(); Bus = null;
    }
    private static void Release(Action action) { try { action(); } catch (System.Runtime.InteropServices.COMException) { } }
    public void Dispose() => Stop();
}
