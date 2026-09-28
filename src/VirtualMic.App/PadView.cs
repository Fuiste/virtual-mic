using System.ComponentModel;
using VirtualMic.Core;

namespace VirtualMic.App;

public sealed class PadView(SoundPad sound, int index) : INotifyPropertyChanged
{
    private bool playing;
    public SoundPad Sound { get; set; } = sound;
    public string Name => Sound.Name;
    public int Index { get; set; } = index;
    public string IndexLabel => $"{Index:00}";
    public string DurationLabel => $"{Sound.Duration:0.0}s";
    public string Color => new[] { "#f49a45", "#b7d784", "#a58be8", "#e968a6", "#8fc7e8", "#ecc174" }[(Index - 1) % 6];
    public string StateLabel => IsPlaying ? "playing" : "";
    public bool IsPlaying { get => playing; set { if (playing == value) return; playing = value; Refresh(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
