using System.Text.Json;

namespace VirtualMic.Core;

public sealed record SoundPad(string Id, string Name, string FileName, double Duration, float Gain = 1, HotkeyChord? Hotkey = null);
public sealed record LibraryState
{
    public int Version { get; init; } = 3;
    public List<SoundPad> Pads { get; init; } = [];
    public string? MicrophoneId { get; init; }
    public string? OutputId { get; init; }
    public string? MonitorId { get; init; }
    public AudioSettings Audio { get; init; } = new();
    public GamingSettings Gaming { get; init; } = new();
}

public sealed class LibraryStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string AudioDirectory => Path.Combine(DirectoryPath, "sounds");
    public string StatePath => Path.Combine(DirectoryPath, "library.json");

    public LibraryState Load()
    {
        if (!File.Exists(StatePath)) return new();
        // A malformed file is surfaced by the UI, never silently replaced.
        var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(StatePath))
            ?? throw new InvalidDataException("the sound library is empty or unreadable");
        if (state.Version is not (1 or 2 or 3) || state.Pads is null || state.Audio is null || state.Gaming is null || state.Pads.Any(p => p is null))
            throw new InvalidDataException("unsupported sound library format");
        if (state.Audio.Effects is not null) Effects.EffectChain.Validate(state.Audio.Effects);
        if (state.Version < 3)
            state = state with { Pads = state.Pads.Select((p, i) => p with { Hotkey = i < 24 ? HotkeyChord.DefaultFor(i) : null }).ToList() };
        GamingSettings.Validate(state.Gaming, state.Pads);
        return state;
    }

    public void Save(LibraryState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = StatePath + ".tmp";
        // Older apps refuse version 3 instead of silently discarding global bindings.
        GamingSettings.Validate(state.Gaming, state.Pads);
        File.WriteAllText(temporary, JsonSerializer.Serialize(state with { Version = 3 }, JsonOptions));
        if (File.Exists(StatePath)) File.Replace(temporary, StatePath, StatePath + ".bak");
        else File.Move(temporary, StatePath);
    }

    public string Resolve(SoundPad pad)
    {
        if (string.IsNullOrWhiteSpace(pad.FileName) || Path.GetFileName(pad.FileName) != pad.FileName)
            throw new InvalidDataException("invalid sound library path");
        return Path.Combine(AudioDirectory, pad.FileName);
    }
}
