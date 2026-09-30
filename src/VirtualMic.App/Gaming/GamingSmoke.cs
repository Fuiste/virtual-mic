using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VirtualMic.Core;

namespace VirtualMic.App.Gaming;

// Off-screen Windows integration checks: real RegisterHotKey/WM_HOTKEY and WPF
// windows, isolated bindings, no keyboard injection or audio endpoints.
internal static class GamingSmoke
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        using var firstSource = new HwndSource(new HwndSourceParameters("virtual mic hotkey test") { Width = 1, Height = 1, PositionX = -10000, PositionY = -10000, WindowStyle = unchecked((int)0x80000000) });
        using var secondSource = new HwndSource(new HwndSourceParameters("virtual mic conflict test") { Width = 1, Height = 1, PositionX = -10000, PositionY = -10000, WindowStyle = unchecked((int)0x80000000) });
        var triggered = new List<string>();
        using var first = new GlobalHotkeys(firstSource, triggered.Add);
        using var second = new GlobalHotkeys(secondSource, _ => { });
        HotkeyChord? available = null;
        foreach (int key in Enumerable.Range(0x70, 11).Concat(Enumerable.Range(0x41, 26)))
        {
            var candidate = new HotkeyChord(key, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);
            first.Apply([new("test-pad", "test pad", candidate)]);
            if (first.RegisteredCount == 1) { available = candidate; break; }
        }
        var chord = available ?? throw new InvalidOperationException("no test hotkey available");
        int oldId = first.IdFor("test-pad");
        nint messageChord = (nint)((chord.Key << 16) | (int)chord.Modifiers);
        PostMessage(firstSource.Handle, 0x0312, oldId, messageChord);
        await Task.Delay(50);
        Check(triggered.SequenceEqual(new[] { "test-pad" }), "hotkey did not dispatch exactly one pad");
        second.Apply([new("conflict", "conflict", chord)]);
        Check(second.RegisteredCount == 0 && second.Errors.ContainsKey("conflict"), "conflict not reported");
        first.Apply([new("new-pad", "new pad", chord)]);
        PostMessage(firstSource.Handle, 0x0312, oldId, messageChord); // stale queued registration must not trigger replacement
        PostMessage(firstSource.Handle, 0x0312, first.IdFor("new-pad"), (nint)((chord.Key << 16) | 1)); // wrong modifiers
        await Task.Delay(50);
        Check(triggered.Count == 1, "stale or wrong-chord message triggered a pad");
        PostMessage(firstSource.Handle, 0x0312, first.IdFor("new-pad"), messageChord);
        await Task.Delay(50); Check(triggered.Last() == "new-pad" && triggered.Count == 2, "rebind didn't dispatch");
        first.Clear(); second.Apply([new("released", "released", chord)]);
        Check(second.RegisteredCount == 1, "hotkey wasn't released"); second.Clear();
        first.Apply([new("a", "a", chord), new("b", "b", chord)]);
        Check(first.RegisteredCount == 1 && first.Errors.ContainsKey("b"), "duplicate binding accepted"); first.Clear();

        nint foreground = OverlayNative.GetForegroundWindow();
        var overlay = new OverlayWindow(diagnostic: true) { Left = -10000, Top = -10000 };
        try
        {
            overlay.Update(true, false, false, []); overlay.Show(); overlay.UpdateLayout();
            nint style = OverlayNative.GetWindowLongPtr(overlay.Handle, -20);
            Check((style & 0x080800a0) == 0x080800a0 && !overlay.ShowActivated && !overlay.ShowInTaskbar, "overlay native focus/click-through styles missing");
            Check(OverlayNative.GetForegroundWindow() == foreground, "overlay stole focus");
            Check(overlay.Height == 42 && overlay.SoundRows == 0 && overlay.StatusText.EndsWith("live"), "idle status wrong");
            Render(overlay.Surface, Path.Combine(directory, "overlay-idle.png"));
            overlay.Update(true, false, false, ["air horn", "mission complete"]); overlay.UpdateLayout();
            Check(overlay.SoundRows == 2 && overlay.Height > 42, "active sound rows missing");
            Render(overlay.Surface, Path.Combine(directory, "overlay-playing.png"));
            overlay.Update(true, true, false, ["a", "b", "c", "d", "e", "f"]);
            Check(overlay.SoundRows == 5 && overlay.StatusText.EndsWith("muted"), "overlap cap/mute wrong");
            overlay.Update(false, false, true, []); Check(overlay.StatusText.EndsWith("audio error") && overlay.Height == 42, "error/stop state wrong");
            overlay.Update(false, false, false, []); Check(overlay.StatusText.EndsWith("stopped"), "stopped state wrong");
            Check(OverlayNative.GetForegroundWindow() == foreground, "overlay updates stole focus");
        }
        finally { overlay.Close(); }
        var pads = Enumerable.Range(0, 24).Select(i => new SoundPad("test-" + i, i == 1 ? "a longer sound name for the layout" : "sound " + (i + 1), "test.wav", 1, Hotkey: HotkeyChord.DefaultFor(i))).ToArray();
        var settings = new GamingWindow(new(), pads) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try { settings.Show(); settings.UpdateLayout(); Render((FrameworkElement)settings.Content, Path.Combine(directory, "gaming-settings.png")); }
        finally { settings.Close(); }
        File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { gamingSmokePassed = true, hotkeyDispatch = true,
            conflicts = true, rebind = true, staleMessages = true, release = true, overlayStyles = true, overlayNoActivation = true,
            overlayStates = true, audioOpened = false, inputInjected = false }));
    }
    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            context.DrawRectangle((Brush)Application.Current.FindResource("Bg"), null, bounds);
            context.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
