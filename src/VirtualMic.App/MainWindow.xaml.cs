using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using VirtualMic.App.Audio;
using VirtualMic.Core;
using VirtualMic.Core.Effects;

namespace VirtualMic.App;

public partial class MainWindow : Window
{
    private readonly AudioEngine engine = new();
    private readonly EffectCatalog effects = new();
    private readonly LibraryStore store;
    private readonly Dictionary<string, float[]> cache = [];
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool initialized;
    private bool importing;
    private bool starting;
    private bool readOnly;
    private bool closed;
    private readonly bool preview;
    private LibraryState saved = new();
    public ObservableCollection<PadView> Pads { get; } = [];

    public MainWindow(bool preview = false, string? dataDirectory = null)
    {
        this.preview = preview;
        store = new LibraryStore(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualMic"));
        InitializeComponent();
        effects.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        if (!preview) effects.LoadDirectory(Path.Combine(store.DirectoryPath, "plugins"));
        EffectsEditor.Load(effects, EffectDefaults.FromLegacy(new()));
        EffectsEditor.Changed += OnControlsChanged;
        EffectsEditor.PluginsRequested += ShowPlugins;
        DataContext = this;
        engine.Faulted += message => Dispatcher.BeginInvoke(() => { if (!closed) { StopEngine(); ShowAudioError(message); } });
        engine.MonitorFaulted += message => Dispatcher.BeginInvoke(() =>
        {
            if (closed) return;
            engine.StopMonitor();
            MonitorToggle.IsChecked = false;
            ShowAudioError($"{message}. mic route stays live; retry listen or stop and choose another output.");
        });
        engine.ReferenceFaulted += message => Dispatcher.BeginInvoke(() =>
        {
            if (!closed) ShowAudioError($"{message}. mic stays live; toggle echo cancellation off/on to retry.");
        });
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveLibrary(); };
        meterTimer.Tick += (_, _) => UpdateMeters();
        if (preview) PopulatePreview();
        else
        {
            try
            {
                saved = store.Load();
                foreach (var sound in saved.Pads.Take(24)) Pads.Add(new(sound, Pads.Count + 1));
                ApplySettings(saved.Audio with { MonitorEnabled = false, MonitorMic = false });
                RefreshDevices();
            }
            catch (Exception ex)
            {
                readOnly = true;
                ShowAudioError($"library could not be loaded; original file preserved. {ex.Message}");
            }
        }
        initialized = true;
        RefreshLibrary();
        UpdateVoiceControls();
        UpdateControlLabels();
        if (effects.Errors.Count > 0) ShowStatus("some plugins could not load. open plugins for details.", true);
        meterTimer.Start();
    }

    private AudioSettings Settings() => new(
        (float)MicVolume.Value / 100, (float)SoundVolume.Value / 100, (float)MasterVolume.Value / 100,
        MuteMic.IsChecked == true, MonitorToggle.IsChecked == true, HearMic.IsChecked == true,
        (float)MonitorVolume.Value / 100, Effects: EffectsEditor.Snapshot());

    private void ApplySettings(AudioSettings s)
    {
        MicVolume.Value = s.MicGain * 100; SoundVolume.Value = s.SoundGain * 100;
        MasterVolume.Value = s.MasterGain * 100; MuteMic.IsChecked = s.MicMuted;
        MonitorToggle.IsChecked = s.MonitorEnabled; HearMic.IsChecked = s.MonitorMic;
        MonitorVolume.Value = s.MonitorGain * 100;
        EffectsEditor.Load(effects, s.Effects ?? EffectDefaults.FromLegacy(s));
    }

    private void RefreshDevices()
    {
        try
        {
            var micId = (Microphone.SelectedItem as AudioDevice)?.Id ?? saved.MicrophoneId;
            var outId = (VirtualOutput.SelectedItem as AudioDevice)?.Id ?? saved.OutputId;
            var monitorId = (MonitorOutput.SelectedItem as AudioDevice)?.Id ?? saved.MonitorId;
            var microphones = AudioEngine.Devices(DataFlow.Capture).Where(x => !x.IsVirtual).ToList();
            var outputs = AudioEngine.Devices(DataFlow.Render);
            var virtuals = outputs.Where(x => x.IsVirtual).ToList();
            var monitors = outputs.Where(x => !x.IsVirtual).ToList();
            Microphone.ItemsSource = microphones;
            VirtualOutput.ItemsSource = virtuals;
            MonitorOutput.ItemsSource = monitors;
            // Missing saved routes stay unselected instead of silently switching devices.
            Microphone.SelectedItem = micId is null ? microphones.FirstOrDefault() : microphones.Find(x => x.Id == micId);
            VirtualOutput.SelectedItem = outId is null
                ? virtuals.FirstOrDefault(x => x.Name.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase))
                    ?? virtuals.FirstOrDefault(x => x.Name.Contains("cable", StringComparison.OrdinalIgnoreCase))
                : virtuals.Find(x => x.Id == outId);
            MonitorOutput.SelectedItem = monitorId is null ? monitors.FirstOrDefault() : monitors.Find(x => x.Id == monitorId);
            UpdateRouteHint();
            CableHelp.Visibility = virtuals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowStatus(virtuals.Count == 0 ? "install vb-cable, then refresh devices." : "select devices, then start virtual mic.", virtuals.Count == 0);
        }
        catch { ShowStatus("audio device access failed. check windows audio service and microphone permissions.", true); }
    }

    private void UpdateRouteHint()
    {
        var destination = VirtualOutput.SelectedItem as AudioDevice;
        if (destination is null) RouteHint.Text = "select a virtual cable.";
        else if (destination.Name.StartsWith("CABLE Input (", StringComparison.OrdinalIgnoreCase) || destination.Name.StartsWith("cable input ·", StringComparison.OrdinalIgnoreCase)) RouteHint.Text = "call app microphone: cable output";
        else RouteHint.Text = "call app microphone: matching recording device";
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) { if (!preview) RefreshDevices(); }
    private void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        UpdateRouteHint(); ScheduleSave();
    }
    private void ControlsChanged(object sender, RoutedEventArgs e) => OnControlsChanged();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => OnControlsChanged();
    private void OnControlsChanged()
    {
        if (!initialized) return;
        UpdateVoiceControls();
        if (MonitorToggle.IsChecked == true && MonitorOutput.SelectedItem is null)
        {
            MonitorToggle.IsChecked = false;
            ShowStatus("select headphones before enabling monitoring.", true);
        }
        if (engine.Bus is not null) engine.UpdateSettings(Settings(), MonitorOutput.SelectedItem as AudioDevice);
        UpdateControlLabels(); ScheduleSave();
    }
    private void UpdateControlLabels()
    {
        MicValue.Text = $"{MicVolume.Value:0}%";
        SoundValue.Text = $"{SoundVolume.Value:0}%";
        MasterValue.Text = $"{MasterVolume.Value:0}%";
        MonitorValue.Text = $"{MonitorVolume.Value:0}%";
    }
    private void UpdateVoiceControls()
    {
        bool cleaningSpeakers = EffectsEditor.Snapshot().Any(s => s.Enabled && effects.Find(s.EffectId)?.Definition.RequiresSpeakerReference == true);
        if (cleaningSpeakers && HearMic.IsChecked == true) HearMic.IsChecked = false;
        HearMic.IsEnabled = !cleaningSpeakers;
        HearMic.ToolTip = cleaningSpeakers ? "disabled during echo cancellation to prevent speaker feedback" : "use headphones to hear your microphone";
    }
    internal void CleanVoicePreview() { EffectsEditor.AddCleanVoice(); UpdateVoiceControls(); }

    private async void ToggleEngine(object sender, RoutedEventArgs e)
    {
        if (preview) { ShowStatus("visual preview only; no audio devices are opened."); return; }
        if (starting || importing) { ShowStatus("finish the current load before starting audio."); return; }
        if (engine.IsRunning) { StopEngine(); ShowStatus("audio stopped."); return; }
        if (Microphone.SelectedItem is not AudioDevice mic || VirtualOutput.SelectedItem is not AudioDevice destination)
        { ShowStatus("select a physical microphone and a virtual output first.", true); return; }
        PowerButton.IsEnabled = false;
        PowerButton.Content = "starting audio…";
        EngineHint.Visibility = Visibility.Collapsed;
        ShowStatus("starting audio…");
        starting = true;
        SetDeviceControls(false);
        try
        {
            // Decode on a worker before opening any endpoint. Audio callbacks do no file I/O.
            foreach (var pad in Pads.ToArray())
                if (!cache.ContainsKey(pad.Sound.Id))
                {
                    try { Cache(pad.Sound.Id, await Task.Run(() => ClipLoader.Read(store.Resolve(pad.Sound)))); }
                    catch { ShowStatus($"{pad.Name}: file unavailable or unsupported; remove and re-add it.", true); }
                }
            if (closed) return;
            EffectsEditor.ClearFaults();
            engine.Start(mic, destination, MonitorOutput.SelectedItem as AudioDevice, Settings(), effects);
            PowerButton.Content = "stop virtual mic";
            LiveText.Text = "live"; LiveDot.Fill = (Brush)FindResource("Green");
            SetDeviceControls(false);
            ScheduleSave();
            ShowStatus("");
        }
        catch (Exception ex) { StopEngine(); ShowAudioError($"could not start audio: {ex.Message}"); }
        finally { starting = false; PowerButton.IsEnabled = true; if (!engine.IsRunning) SetDeviceControls(true); }
    }
    private void SetDeviceControls(bool enabled)
    { Microphone.IsEnabled = VirtualOutput.IsEnabled = MonitorOutput.IsEnabled = RefreshButton.IsEnabled = enabled; }
    private void StopEngine()
    {
        engine.Stop();
        PowerButton.Content = "start virtual mic";
        LiveText.Text = preview ? "preview · audio off" : "stopped";
        LiveDot.Fill = (Brush)FindResource("Muted");
        EngineHint.Visibility = Visibility.Collapsed;
        SetDeviceControls(true);
        UpdateMeters();
    }

    private void PadClicked(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PadView pad) Play(pad); }
    private void Play(PadView pad)
    {
        if (preview) { ShowStatus($"{pad.Name} selected — preview is silent."); return; }
        if (!engine.IsRunning || engine.Bus is null) { ShowStatus("start virtual mic before playing a sound."); return; }
        if (!cache.TryGetValue(pad.Sound.Id, out var samples)) { ShowStatus($"{pad.Name} is unavailable. stop audio and re-add the file.", true); return; }
        engine.Bus.Play(pad.Sound.Id, samples, pad.Sound.Gain);
        ShowStatus($"playing / {pad.Name}");
    }
    private void StopSounds(object sender, RoutedEventArgs e)
    {
        engine.Bus?.StopSounds();
        foreach (var pad in Pads) pad.IsPlaying = false;
        ShowStatus("sounds stopped.");
    }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (e.Key == Key.Escape) { StopSounds(this, new()); e.Handled = true; return; }
        int index = e.Key >= Key.D1 && e.Key <= Key.D9 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (index >= 0 && index < Pads.Count) { Play(Pads[index]); e.Handled = true; }
    }

    private async void AddSounds(object sender, RoutedEventArgs e)
    {
        if (preview || readOnly || starting) { ShowStatus("file imports are disabled in this view or while audio is starting."); return; }
        var picker = new OpenFileDialog { Multiselect = true, Title = "add sound files", Filter = "audio files|*.wav;*.mp3;*.aiff;*.aif" };
        if (picker.ShowDialog(this) == true) await ImportFiles(picker.FileNames);
    }
    private async void FilesDropped(object sender, DragEventArgs e)
    {
        if (!preview && !readOnly && !starting && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportFiles(paths);
    }
    private async Task ImportFiles(string[] paths)
    {
        if (importing) { ShowStatus("an import is already running."); return; }
        importing = true;
        int added = 0;
        var errors = new List<string>();
        try
        {
            Directory.CreateDirectory(store.AudioDirectory);
            foreach (var path in paths)
            {
                if (Pads.Count >= 24) { errors.Add("library limit: 24 pads"); break; }
                try
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is not (".wav" or ".mp3" or ".aiff" or ".aif")) throw new InvalidDataException("use wav, mp3 or aiff");
                    var samples = await Task.Run(() => ClipLoader.Read(path));
                    if (closed) return;
                    var sound = new SoundPad(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(path), Guid.NewGuid().ToString("N") + ".wav", samples.Length / 96000.0);
                    Cache(sound.Id, samples);
                    try { await Task.Run(() => ClipLoader.Write(store.Resolve(sound), samples)); }
                    catch { cache.Remove(sound.Id); throw; }
                    Pads.Add(new(sound, Pads.Count + 1)); added++;
                    SaveLibrary();
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            }
            RefreshLibrary();
            ShowStatus($"added {added} sound{(added == 1 ? "" : "s")}." + (errors.Count > 0 ? " " + string.Join(" / ", errors) : ""), errors.Count > 0);
        }
        finally { importing = false; }
    }
    private void Cache(string id, float[] samples)
    {
        long total = cache.Where(p => p.Key != id).Sum(p => (long)p.Value.Length * sizeof(float));
        if (total + (long)samples.Length * sizeof(float) > ClipLoader.MaxCacheBytes)
            throw new InvalidDataException("decoded sound library exceeds the 256 mib memory budget");
        cache[id] = samples;
    }

    private void AddStarterSounds(object sender, RoutedEventArgs e)
    {
        if (preview || readOnly || importing || starting) return;
        try
        {
            Directory.CreateDirectory(store.AudioDirectory);
            string[] names = ["level up", "downer", "low orbit", "arcade"];
            for (int i = 0; i < names.Length && Pads.Count < 24; i++)
            {
                var samples = ClipLoader.Tone(i);
                var sound = new SoundPad(Guid.NewGuid().ToString("N"), names[i], Guid.NewGuid().ToString("N") + ".wav", samples.Length / 96000.0);
                ClipLoader.Write(store.Resolve(sound), samples); Cache(sound.Id, samples);
                Pads.Add(new(sound, Pads.Count + 1));
            }
            RefreshLibrary(); SaveLibrary(); ShowStatus("sample sounds added.");
        }
        catch (Exception ex) { ShowStatus($"could not add starter sounds: {ex.Message}", true); }
    }

    private static PadView? ContextPad(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is Button button ? button.Tag as PadView : null;
    private void RemovePad(object sender, RoutedEventArgs e)
    {
        if (readOnly || starting) return;
        if (ContextPad(sender) is not { } pad) return;
        engine.Bus?.StopSound(pad.Sound.Id);
        Pads.Remove(pad); cache.Remove(pad.Sound.Id);
        // Retain the copied file for recovery, never delete the user's original.
        RefreshLibrary(); ScheduleSave(); ShowStatus("sound removed.");
    }
    private void RenamePad(object sender, RoutedEventArgs e)
    {
        if (readOnly || starting || ContextPad(sender) is not { } pad) return;
        var field = new TextBox { Text = pad.Name, Margin = new Thickness(0, 0, 0, 18), Padding = new Thickness(8), MaxLength = 60 };
        var done = new Button { Content = "save name", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(field); panel.Children.Add(done);
        var dialog = new Window { Title = "rename sound", Owner = this, Width = 360, Height = 170, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Bg"), Content = panel };
        done.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(field.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { field.Focus(); field.SelectAll(); };
        if (dialog.ShowDialog() == true) { pad.Sound = pad.Sound with { Name = field.Text.Trim() }; pad.Refresh(); ScheduleSave(); }
    }
    private void RefreshLibrary()
    {
        for (int i = 0; i < Pads.Count; i++) { Pads[i].Index = i + 1; Pads[i].Refresh(); }
        EmptyLibrary.Visibility = Pads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibraryCount.Text = $"{Pads.Count} / 24 sounds";
    }
    private void UpdateMeters()
    {
        if (engine.Bus is { } bus)
            while (bus.TryDequeueEffectFault(out var fault))
                if (fault is not null) { EffectsEditor.ReportFault(fault); ShowStatus(fault.Message, true); }
        var levels = engine.Bus?.Levels ?? new AudioLevels(0, 0, 0, false);
        MicMeter.Value = Meter(levels.Mic); OutputMeter.Value = Meter(levels.Output);
        LevelText.Text = levels.Output > .00001 ? $"{20 * Math.Log10(levels.Output):0.0} dbfs" : "−∞ dbfs";
        LimitText.Text = levels.Limited ? "limiting · lower gain" : "";
        LimitText.Foreground = (Brush)FindResource(levels.Limited ? "Accent" : "Muted");
        var playing = engine.Bus?.PlayingIds ?? [];
        foreach (var pad in Pads) pad.IsPlaying = playing.Contains(pad.Sound.Id);
    }
    private static double Meter(float level) => level <= .001f ? 0 : Math.Clamp((20 * Math.Log10(level) + 60) / 60, 0, 1);
    private void ScheduleSave() { if (preview || !initialized || readOnly) return; saveTimer.Stop(); saveTimer.Start(); }
    private void SaveLibrary()
    {
        if (preview || readOnly) return;
        try
        {
            saved = new LibraryState { Pads = Pads.Select(x => x.Sound).ToList(), Audio = Settings(),
                MicrophoneId = (Microphone.SelectedItem as AudioDevice)?.Id ?? saved.MicrophoneId,
                OutputId = (VirtualOutput.SelectedItem as AudioDevice)?.Id ?? saved.OutputId,
                MonitorId = (MonitorOutput.SelectedItem as AudioDevice)?.Id ?? saved.MonitorId };
            store.Save(saved);
        }
        catch (Exception ex) { ShowStatus($"could not save the library: {ex.Message}", true); }
    }
    private void ShowStatus(string text, bool error = false)
    { Status.Text = text; Status.Foreground = (Brush)FindResource(error ? "Accent" : "Muted"); }
    private void ShowAudioError(string text)
    {
        EngineHint.Text = text;
        EngineHint.Visibility = Visibility.Visible;
        EngineHint.Foreground = (Brush)FindResource("Accent");
        ShowStatus(text, true);
    }
    private void OpenCableHelp(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }); } catch { ShowStatus("visit https://vb-audio.com/cable/ to get the driver."); } }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (importing) { e.Cancel = true; ShowStatus("finishing the import; close again when it completes."); return; }
        closed = true; meterTimer.Stop(); saveTimer.Stop(); engine.Dispose(); SaveLibrary(); effects.Dispose();
    }

    private void ShowPlugins()
    {
        string directory = Path.Combine(store.DirectoryPath, "plugins");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "installed effects", FontSize = 16, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var effect in effects.Effects)
            panel.Children.Add(new TextBlock { Text = $"{effect.Definition.Name} / {effect.Source}", Margin = new Thickness(0, 3, 0, 3) });
        foreach (var error in effects.Errors)
            panel.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Accent"), Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "bundled plugins load beside the app. put custom plugins in the user folder, then restart. plugins run with your windows permissions; only install code you trust.",
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 12) });
        var folder = new Button { Content = "open plugins folder", HorizontalAlignment = HorizontalAlignment.Left };
        folder.Click += (_, _) =>
        {
            if (preview) return;
            try { Directory.CreateDirectory(directory); Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        };
        panel.Children.Add(folder);
        var guide = new Button { Content = "plugin development guide", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        guide.Click += (_, _) =>
        {
            try
            {
                string local = Path.Combine(AppContext.BaseDirectory, "docs", "plugins.md");
                Process.Start(new ProcessStartInfo(File.Exists(local) ? local : "https://github.com/Fuiste/virtual-mic/blob/master/docs/plugins.md") { UseShellExecute = true });
            }
            catch (Exception ex) { ShowStatus(ex.Message, true); }
        };
        panel.Children.Add(guide);
        new Window { Title = "plugins", Owner = this, Width = 480, Height = 440, Background = (Brush)FindResource("Bg"),
            Foreground = (Brush)FindResource("Ink"), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }.ShowDialog();
    }

    private void PopulatePreview()
    {
        string[] names = ["air horn", "dramatic pause", "bruh", "tiny applause", "sad trombone", "mission complete"];
        double[] durations = [1.8, 3.2, .8, 2.4, 2.9, 1.6];
        for (int i = 0; i < names.Length; i++) Pads.Add(new(new($"preview-{i}", names[i], "preview.wav", durations[i]), i + 1));
        Microphone.ItemsSource = new[] { new AudioDevice("preview-mic", "usb microphone", false) };
        VirtualOutput.ItemsSource = new[] { new AudioDevice("preview-cable", "cable input · vb-audio", true) };
        MonitorOutput.ItemsSource = new[] { new AudioDevice("preview-headphones", "headphones", false) };
        Microphone.SelectedIndex = VirtualOutput.SelectedIndex = MonitorOutput.SelectedIndex = 0;
        EffectsEditor.Load(effects, EffectDefaults.FromLegacy(new(BassEnabled: true)));
        MonitorToggle.IsChecked = true;
        LiveText.Text = "preview / audio off";
        ShowStatus("visual preview · example pads and devices · audio is off");
    }

    internal void VerifyUi()
    {
        EffectsEditor.VerifyControls();
        EffectsEditor.Load(effects, EffectDefaults.FromLegacy(new(BassEnabled: true)));
        var chain = EffectsEditor.Snapshot();
        chain[0] = chain[0] with { Parameters = new() { ["gain"] = 12 }, Target = EffectTarget.Both };
        chain[1] = chain[1] with { Enabled = true, Target = EffectTarget.Sounds, Parameters = new() { ["drive"] = 3, ["mix"] = 65 } };
        EffectsEditor.Load(effects, chain);
        if (Settings().Effects![0].Parameters["gain"] != 12 || Settings().Effects![1].Target != EffectTarget.Sounds)
            throw new InvalidOperationException("effect controls did not update settings");
        Play(Pads[0]);
        if (!Status.Text.Contains("preview is silent")) throw new InvalidOperationException("preview must stay silent");
        if (engine.IsRunning) throw new InvalidOperationException("preview opened audio");
        StopSounds(this, new());
        EffectsEditor.Load(effects, EffectDefaults.FromLegacy(new(BassEnabled: true)));
        ShowStatus("visual preview · example pads and devices · audio is off");
    }

    internal void EmptyPreview() { Pads.Clear(); RefreshLibrary(); }
    internal void ErrorPreview() => ShowAudioError("could not start audio: microphone disconnected. reconnect it, refresh devices, and try again.");

    internal async Task VerifyLibraryLifecycle()
    {
        Directory.CreateDirectory(store.DirectoryPath);
        string source = Path.Combine(store.DirectoryPath, "source-44100-mono.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(source, new NAudio.Wave.WaveFormat(44100, 16, 1)))
            for (int i = 0; i < 44100; i++) writer.WriteSample(.2f * MathF.Sin(i * 2 * MathF.PI * 440 / 44100));
        byte[] original = File.ReadAllBytes(source);
        await ImportFiles([source]);
        if (Pads.Count != 1 || Math.Abs(Pads[0].Sound.Duration - 1) > .01)
            throw new InvalidOperationException("mono 44.1 khz import or resampling failed");
        if (!File.ReadAllBytes(source).SequenceEqual(original)) throw new InvalidOperationException("import altered source audio");
        var decoded = ClipLoader.Read(store.Resolve(Pads[0].Sound));
        if (decoded.Where((_, i) => i % 2 == 0).Zip(decoded.Where((_, i) => i % 2 == 1)).Any(p => p.First != p.Second))
            throw new InvalidOperationException("mono import has unequal stereo channels");
        string bad = Path.Combine(store.DirectoryPath, "invalid.wav");
        File.WriteAllText(bad, "this is not an audio file");
        await ImportFiles([bad]);
        if (Pads.Count != 1) throw new InvalidOperationException("invalid audio entered the library");
        AddStarterSounds(this, new());
        EffectsEditor.Load(effects, EffectDefaults.FromLegacy(new(BassEnabled: true, BassDb: 11)));
        SaveLibrary();
        var restored = store.Load();
        if (restored.Pads.Count != 5 || restored.Audio.Effects![0].Parameters["gain"] != 11 || !restored.Audio.Effects[0].Enabled)
            throw new InvalidOperationException("library or effects did not persist");
        if (engine.IsRunning) throw new InvalidOperationException("test unexpectedly started audio");
    }
}
