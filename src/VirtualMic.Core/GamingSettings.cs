using System.Text.Json.Serialization;

namespace VirtualMic.Core;

[Flags]
public enum HotkeyModifiers { Alt = 1, Control = 2, Shift = 4 }

// Windows virtual keys, independent of WPF so persistence and validation are testable.
public sealed record HotkeyChord(int Key, HotkeyModifiers Modifiers)
{
    [JsonIgnore] public bool IsValid => (Modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift)) == 0
        && (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) != 0 && KeyName(Key) is not null;
    [JsonIgnore] public string Label => string.Concat(Modifiers.HasFlag(HotkeyModifiers.Control) ? "ctrl+" : "",
        Modifiers.HasFlag(HotkeyModifiers.Alt) ? "alt+" : "", Modifiers.HasFlag(HotkeyModifiers.Shift) ? "shift+" : "", KeyName(Key));
    public static string? KeyName(int key) => key switch
    {
        >= 0x70 and <= 0x7a => "f" + (key - 0x70 + 1), // F12 is reserved by Windows.
        >= 0x30 and <= 0x39 => ((char)key).ToString(),
        >= 0x41 and <= 0x5a => ((char)(key + 32)).ToString(),
        >= 0x60 and <= 0x69 => "num " + (key - 0x60),
        0x08 => "backspace", 0x20 => "space", 0x21 => "page up", 0x22 => "page down",
        0x23 => "end", 0x24 => "home", 0x2d => "insert", 0x2e => "delete", _ => null
    };
    public static HotkeyChord DefaultFor(int index)
    {
        if (index is < 0 or >= 24) throw new ArgumentOutOfRangeException(nameof(index));
        return new(index % 12 == 11 ? 0x30 : 0x70 + index % 12,
            HotkeyModifiers.Control | HotkeyModifiers.Alt | (index >= 12 ? HotkeyModifiers.Shift : 0));
    }
}

public enum OverlayCorner { TopRight, TopLeft, BottomRight, BottomLeft }

public sealed record GamingSettings
{
    public bool HotkeysEnabled { get; init; } = true;
    public bool OverlayEnabled { get; init; } = true;
    public OverlayCorner OverlayCorner { get; init; } = OverlayCorner.TopRight;
    // Null follows the foreground window's display; device names survive monitor reordering.
    public string? OverlayDisplay { get; init; }
    public HotkeyChord? StopSounds { get; init; } = new(0x23, HotkeyModifiers.Control | HotkeyModifiers.Alt);
    public HotkeyChord? ToggleOverlay { get; init; } = new(0x4f, HotkeyModifiers.Control | HotkeyModifiers.Alt);
    public static void Validate(GamingSettings gaming, IEnumerable<SoundPad> pads)
    {
        if (!Enum.IsDefined(gaming.OverlayCorner) || gaming.OverlayDisplay?.Length > 128 ||
            pads.Select(p => p.Hotkey).Concat([gaming.StopSounds, gaming.ToggleOverlay]).Any(c => c is not null && !c.IsValid))
            throw new InvalidDataException("invalid gaming settings or hotkey; use ctrl/alt with a supported key (except f12)");
    }
}
