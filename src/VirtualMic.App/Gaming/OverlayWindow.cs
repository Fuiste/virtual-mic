using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VirtualMic.Core;

namespace VirtualMic.App.Gaming;

internal sealed class OverlayWindow : Window
{
    private readonly TextBlock status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse dot = new() { Width = 7, Height = 7, Margin = new(0, 0, 8, 0) };
    private readonly StackPanel sounds = new() { Margin = new(15, 7, 0, 0), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer placement = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private GamingSettings settings = new();
    private HwndSource? source;
    private string lastStatus = "", lastSounds = "";
    private nint lastMonitor;
    private nint lastForeground, selectedMonitor;
    private readonly bool diagnostic;
    private int lastX = int.MinValue, lastY, lastWidth, lastHeight;
    public FrameworkElement Surface { get; }
    public nint Handle => source?.Handle ?? 0;
    internal string StatusText => status.Text;
    internal int SoundRows => sounds.Children.Count;
    public OverlayWindow(bool diagnostic = false)
    {
        this.diagnostic = diagnostic;
        Title = "virtual mic overlay"; Width = 260; Height = 42;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot); header.Children.Add(status);
        var panel = new StackPanel(); panel.Children.Add(header); panel.Children.Add(sounds);
        Surface = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 21, 25, 23)),
            BorderBrush = (Brush)Application.Current.FindResource("Line"), BorderThickness = new(1),
            CornerRadius = new(5), Padding = new(13, 11, 13, 11), Child = panel };
        Content = Surface;
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            nint style = OverlayNative.GetWindowLongPtr(Handle, -20);
            // Layered + transparent is OS-level click-through even over another
            // process. NOACTIVATE/toolwindow keep game focus and Alt+Tab untouched.
            OverlayNative.SetWindowLongPtr(Handle, -20, (style | 0x080800a0) & ~0x00040000);
            source.AddHook(Hook);
        };
        placement.Tick += (_, _) => Place();
        IsVisibleChanged += (_, _) => { if (IsVisible && !diagnostic) { Place(); placement.Start(); } else placement.Stop(); };
        Closed += (_, _) => { placement.Stop(); source?.RemoveHook(Hook); };
        Update(false, false, false, []);
    }
    public void Configure(GamingSettings value)
    {
        settings = value; lastX = int.MinValue;
        selectedMonitor = OverlayNative.Displays().FirstOrDefault(d => d.Device == value.OverlayDisplay)?.Handle ?? 0;
        placement.Stop(); if (IsVisible && !diagnostic) placement.Start();
        if (IsVisible) Place();
    }
    public void Update(bool live, bool muted, bool error, IReadOnlyList<string> names)
    {
        string nextStatus = error ? "virtual mic · audio error" : live ? muted ? "virtual mic · mic muted" : "virtual mic · live" : "virtual mic · stopped";
        if (nextStatus != lastStatus)
        {
            lastStatus = nextStatus; status.Text = nextStatus;
            status.Foreground = (Brush)Application.Current.FindResource("Ink");
            dot.Fill = (Brush)Application.Current.FindResource(error || muted ? "Accent" : live ? "Green" : "Muted");
        }
        string nextSounds = string.Join('\n', names);
        if (nextSounds == lastSounds) return;
        lastSounds = nextSounds; sounds.Children.Clear();
        foreach (string name in names.Take(4))
            sounds.Children.Add(new TextBlock { Text = "▶ " + name, FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("Green"), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new(0, 3, 0, 3), MaxWidth = 215 });
        if (names.Count > 4) sounds.Children.Add(new TextBlock { Text = $"+{names.Count - 4} sounds", FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new(0, 3, 0, 3) });
        sounds.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Height = names.Count == 0 ? 42 : 49 + Math.Min(names.Count, 4) * 23 + (names.Count > 4 ? 22 : 0);
        if (IsVisible) Place();
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x0084 or 0x0021) { handled = true; return message == 0x0084 ? -1 : 3; } // HTTRANSPARENT, MA_NOACTIVATE
        if (message == 0x007e) { selectedMonitor = 0; Configure(settings); }
        if (message is 0x007e or 0x02e0) { lastX = int.MinValue; Dispatcher.BeginInvoke(Place); } // display/DPI change
        return 0;
    }
    private void Place()
    {
        if (Handle == 0 || diagnostic) return;
        nint foreground = OverlayNative.GetForegroundWindow();
        nint monitor = selectedMonitor != 0 ? selectedMonitor : OverlayNative.MonitorFromWindow(foreground, 2);
        if (!OverlayNative.TryInfo(monitor, out var info))
        { selectedMonitor = 0; monitor = OverlayNative.MonitorFromWindow(foreground, 2); if (!OverlayNative.TryInfo(monitor, out info)) return; }
        double scale = OverlayNative.Scale(monitor);
        int width = (int)Math.Ceiling(Width * scale), height = (int)Math.Ceiling(Height * scale), margin = (int)(16 * scale);
        bool right = settings.OverlayCorner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        bool bottom = settings.OverlayCorner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        int x = right ? info.Work.Right - width - margin : info.Work.Left + margin;
        int y = bottom ? info.Work.Bottom - height - margin : info.Work.Top + margin;
        if (foreground == lastForeground && monitor == lastMonitor && x == lastX && y == lastY && width == lastWidth && height == lastHeight) return;
        lastForeground = foreground;
        lastMonitor = monitor; lastX = x; lastY = y; lastWidth = width; lastHeight = height;
        OverlayNative.SetWindowPos(Handle, -1, x, y, width, height, 0x0010 | 0x0200); // topmost, no activation
    }
}

internal static class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        public int Size;
        public Rectangle Monitor, Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    internal sealed record Display(nint Handle, string Device, string Label);
    internal static Display[] Displays()
    {
        var displays = new List<Display>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref Rectangle rectangle, nint data) =>
        {
            if (TryInfo(monitor, out var info)) displays.Add(new(monitor, info.Device, "display " + (displays.Count + 1) + ((info.Flags & 1) != 0 ? " (primary)" : "")));
            return true;
        }, 0);
        return displays.ToArray();
    }
    internal static bool TryInfo(nint monitor, out MonitorInfo info)
    { info = new() { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" }; return GetMonitorInfo(monitor, ref info); }
    internal static double Scale(nint monitor) => GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 ? x / 96.0 : 1;
    private delegate bool MonitorCallback(nint monitor, nint hdc, ref Rectangle rectangle, nint data);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorCallback callback, nint data);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
