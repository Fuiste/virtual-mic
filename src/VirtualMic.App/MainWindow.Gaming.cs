using System.Windows;
using System.Windows.Interop;
using VirtualMic.App.Gaming;
using VirtualMic.Core;

namespace VirtualMic.App;

public partial class MainWindow
{
    private GamingSettings gaming = new();
    private GlobalHotkeys? hotkeys;
    private OverlayWindow? overlay;
    private readonly string?[] playingIds = new string?[16];
    private string[] playingNames = [];
    private MixBus? displayedBus;
    private long displayedRevision = -1;
    private bool audioError;
    private bool gamingDialogOpen;

    private void InitializeGaming()
    {
        gaming = saved.Gaming;
        OverlayToggle.IsChecked = gaming.OverlayEnabled;
        Activated += (_, _) => UpdateGamingState();
        Deactivated += (_, _) => UpdateGamingState();
        StateChanged += (_, _) => UpdateGamingState();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (preview) return;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hotkeys = new(source, TriggerGlobal);
        RefreshHotkeys();
    }
    private PadView NewPad(SoundPad sound)
    {
        var used = Pads.Select(p => p.Sound.Hotkey).Concat([gaming.StopSounds, gaming.ToggleOverlay]).ToHashSet();
        var chord = Enumerable.Range(0, 24).Select(HotkeyChord.DefaultFor).FirstOrDefault(c => !used.Contains(c));
        return new(sound with { Hotkey = chord }, Pads.Count + 1);
    }
    private IEnumerable<HotkeyBinding> HotkeyBindings()
    {
        if (gaming.StopSounds is { } stop) yield return new("action:stop", "stop all sounds", stop);
        if (gaming.ToggleOverlay is { } toggle) yield return new("action:overlay", "toggle overlay", toggle);
        foreach (var p in Pads)
            if (p.Sound.Hotkey is { } chord) yield return new(p.Sound.Id, p.Name, chord);
    }
    private void RefreshHotkeys()
    {
        if (hotkeys is null || closed) return;
        hotkeys.Apply(gaming.HotkeysEnabled && !readOnly && !gamingDialogOpen ? HotkeyBindings() : []);
        foreach (var pad in Pads)
        {
            pad.HotkeyError = hotkeys.Errors.GetValueOrDefault(pad.Sound.Id);
            pad.Refresh();
        }
        if (hotkeys.Errors.Count > 0) ShowStatus($"{hotkeys.Errors.Count} hotkey(s) unavailable. open gaming to rebind.", true);
    }
    private void TriggerGlobal(string id)
    {
        if (closed || gamingDialogOpen) return;
        if (id == "action:stop") StopSounds(this, new());
        else if (id == "action:overlay") OverlayToggle.IsChecked = !gaming.OverlayEnabled;
        else if (Pads.FirstOrDefault(p => p.Sound.Id == id) is { } pad) Play(pad);
    }
    private void GamingControlsChanged(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        gaming = gaming with { OverlayEnabled = OverlayToggle.IsChecked == true };
        overlay?.Configure(gaming); UpdateGamingState(); ScheduleSave();
    }
    private void OpenGaming(object sender, RoutedEventArgs e)
    {
        if (readOnly || starting) return;
        var dialog = new GamingWindow(gaming, Pads.Select(p => p.Sound), hotkeys?.Errors) { Owner = this };
        gamingDialogOpen = true; hotkeys?.Clear();
        try
        {
            if (dialog.ShowDialog() == true)
            {
                gaming = dialog.Result;
                foreach (var sound in dialog.ResultPads)
                    if (Pads.FirstOrDefault(p => p.Sound.Id == sound.Id) is { } pad) { pad.Sound = sound; pad.Refresh(); }
                OverlayToggle.IsChecked = gaming.OverlayEnabled;
                overlay?.Configure(gaming); SaveLibrary();
            }
        }
        finally { gamingDialogOpen = false; RefreshHotkeys(); UpdateGamingState(); }
    }
    private void RefreshPlayback(bool force = false)
    {
        var bus = engine.Bus;
        long revision = bus?.PlaybackRevision ?? 0;
        if (!force && ReferenceEquals(bus, displayedBus) && revision == displayedRevision) return;
        displayedBus = bus; displayedRevision = revision;
        int count = bus?.CopyPlayingIds(playingIds) ?? 0;
        foreach (var pad in Pads)
        {
            bool playing = false;
            for (int i = 0; i < count; i++) if (playingIds[i] == pad.Sound.Id) { playing = true; break; }
            pad.IsPlaying = playing;
        }
        playingNames = Pads.Where(p => p.IsPlaying).Select(p => p.Name).ToArray();
        overlay?.Update(engine.IsRunning, MuteMic.IsChecked == true, audioError, playingNames);
    }
    private void UpdateGamingState()
    {
        if (!initialized || preview || closed) return;
        bool show = gaming.OverlayEnabled && !IsActive && IsVisible && !gamingDialogOpen;
        if (show)
        {
            if (overlay is null) { overlay = new(); overlay.Configure(gaming); }
            overlay.Update(engine.IsRunning, MuteMic.IsChecked == true, audioError, playingNames);
            if (!overlay.IsVisible) overlay.Show();
        }
        else overlay?.Hide();
        if (!engine.IsRunning) { meterTimer.Stop(); return; }
        // No meter redraws while minimized; overlay playback changes need at most
        // 10 Hz polling. Hidden audio with the overlay off checks faults at 4 Hz.
        meterTimer.Interval = TimeSpan.FromMilliseconds(WindowState != WindowState.Minimized ? 50 : show ? 100 : 250);
        meterTimer.Start();
        overlay?.Update(true, MuteMic.IsChecked == true, audioError, playingNames);
    }
    private void CloseGaming() { hotkeys?.Dispose(); hotkeys = null; overlay?.Close(); overlay = null; }
}
