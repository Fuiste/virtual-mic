using System.Runtime.InteropServices;
using System.Windows.Interop;
using VirtualMic.Core;

namespace VirtualMic.App.Gaming;

internal sealed record HotkeyBinding(string Id, string Name, HotkeyChord Chord);

// RegisterHotKey delivers messages only on a press (MOD_NOREPEAT). No keyboard
// hook, key-state polling, audio/file work, or elevated process is involved.
internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, HotkeyBinding> registered = [];
    private readonly Action<string> trigger;
    private int nextId = 0x5000;
    public Dictionary<string, string> Errors { get; } = [];
    public int RegisteredCount => registered.Count;
    internal int IdFor(string bindingId) => registered.Single(p => p.Value.Id == bindingId).Key;
    public GlobalHotkeys(HwndSource source, Action<string> trigger)
    {
        this.source = source; this.trigger = trigger; source.AddHook(Hook);
    }
    public void Apply(IEnumerable<HotkeyBinding> bindings)
    {
        Clear(); Errors.Clear();
        var used = new HashSet<HotkeyChord>();
        foreach (var binding in bindings)
        {
            if (!binding.Chord.IsValid || !used.Add(binding.Chord))
            { Errors[binding.Id] = "invalid or duplicate hotkey"; continue; }
            int id = nextId++;
            if (nextId > 0xbfff) nextId = 0x5000;
            if (RegisterHotKey(source.Handle, id, (uint)binding.Chord.Modifiers | 0x4000, (uint)binding.Chord.Key))
                registered.Add(id, binding);
            else Errors[binding.Id] = $"{binding.Chord.Label} unavailable (windows error {Marshal.GetLastWin32Error()}); choose another hotkey";
        }
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && registered.TryGetValue((int)wParam, out var binding))
        {
            // Ignore queued messages from a binding that changed while a modal
            // settings dialog was open; don't trigger the newly assigned sound.
            long chord = lParam.ToInt64();
            if ((int)((chord >> 16) & 0xffff) == binding.Chord.Key && (chord & 7) == (int)binding.Chord.Modifiers)
            { handled = true; trigger(binding.Id); }
        }
        return 0;
    }
    public void Clear()
    {
        foreach (int id in registered.Keys) UnregisterHotKey(source.Handle, id);
        registered.Clear();
    }
    public void Dispose() { Clear(); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
