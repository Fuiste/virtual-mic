using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VirtualMic.Core;

namespace VirtualMic.App.Gaming;

internal sealed class GamingWindow : Window
{
    private readonly CheckBox hotkeys = new() { Content = "global hotkeys" };
    private readonly CheckBox overlay = new() { Content = "overlay" };
    private readonly ComboBox corner = new();
    private readonly ComboBox display = new();
    private readonly StackPanel bindings = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Accent") };
    private readonly List<SoundPad> pads;
    private readonly Dictionary<string, HotkeyChord?> chords = [];
    private readonly IReadOnlyDictionary<string, string> registrationErrors;
    private readonly GamingSettings original;
    public GamingSettings Result { get; private set; }
    public List<SoundPad> ResultPads => pads.Select(p => p with { Hotkey = chords[p.Id] }).ToList();
    public FrameworkElement Surface { get; }
    public GamingWindow(GamingSettings settings, IEnumerable<SoundPad> sounds, IReadOnlyDictionary<string, string>? errors = null)
    {
        original = Result = settings; pads = sounds.ToList(); registrationErrors = errors ?? new Dictionary<string, string>();
        Title = "gaming"; Width = 540; Height = 640; MinWidth = 500; MinHeight = 420;
        Background = (Brush)Application.Current.FindResource("Bg"); Foreground = (Brush)Application.Current.FindResource("Ink");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        hotkeys.IsChecked = settings.HotkeysEnabled; overlay.IsChecked = settings.OverlayEnabled;
        foreach (var p in pads) chords[p.Id] = p.Hotkey;
        chords["action:stop"] = settings.StopSounds; chords["action:overlay"] = settings.ToggleOverlay;
        var panel = new StackPanel { Margin = new(22) };
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 18) };
        hotkeys.Margin = new(0, 0, 24, 0); switches.Children.Add(hotkeys); switches.Children.Add(overlay); panel.Children.Add(switches);
        panel.Children.Add(Label("overlay display"));
        var displays = OverlayNative.Displays();
        display.Items.Add(new DisplayOption(null, "follow active display"));
        foreach (var d in displays) display.Items.Add(new DisplayOption(d.Device, d.Label));
        if (settings.OverlayDisplay is { } saved && !displays.Any(d => d.Device == saved)) display.Items.Add(new DisplayOption(saved, "saved display (disconnected)"));
        display.SelectedItem = display.Items.Cast<DisplayOption>().First(d => d.Device == settings.OverlayDisplay); panel.Children.Add(display);
        panel.Children.Add(Label("overlay corner"));
        foreach (var c in Enum.GetValues<OverlayCorner>()) corner.Items.Add(new CornerOption(c, c switch
        { OverlayCorner.TopLeft => "top left", OverlayCorner.TopRight => "top right", OverlayCorner.BottomLeft => "bottom left", _ => "bottom right" }));
        corner.SelectedItem = corner.Items.Cast<CornerOption>().First(c => c.Corner == settings.OverlayCorner); panel.Children.Add(corner);
        panel.Children.Add(new TextBlock { Text = "windowed / borderless games · click-through · no focus change", FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new(0, 10, 0, 12), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(bindings); BuildBindings();
        var reset = new Button { Content = "restore default hotkeys", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 14, 0, 8) };
        reset.Click += (_, _) =>
        {
            for (int i = 0; i < pads.Count; i++) chords[pads[i].Id] = HotkeyChord.DefaultFor(i);
            chords["action:stop"] = new GamingSettings().StopSounds; chords["action:overlay"] = new GamingSettings().ToggleOverlay;
            BuildBindings(); error.Text = "";
        };
        var footer = new StackPanel { Margin = new(22, 0, 22, 14) };
        footer.Children.Add(error);
        var buttons = new DockPanel { LastChildFill = false };
        buttons.Children.Add(reset);
        var save = new Button { Content = "save", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        save.Click += (_, _) =>
        {
            if (chords.Values.Where(c => c is not null).GroupBy(c => c).Any(g => g.Count() > 1)) { error.Text = "each hotkey must be unique."; return; }
            Result = original with { HotkeysEnabled = hotkeys.IsChecked == true, OverlayEnabled = overlay.IsChecked == true,
                OverlayCorner = ((CornerOption)corner.SelectedItem).Corner, OverlayDisplay = ((DisplayOption)display.SelectedItem).Device,
                StopSounds = chords["action:stop"], ToggleOverlay = chords["action:overlay"] };
            DialogResult = true;
        };
        DockPanel.SetDock(save, Dock.Right); buttons.Children.Add(save); footer.Children.Add(buttons);
        var layout = new Grid { Background = Background }; layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetRow(footer, 1); layout.Children.Add(footer);
        Surface = layout; Content = layout;
    }
    private void BuildBindings()
    {
        bindings.Children.Clear();
        AddBinding("action:stop", "stop all sounds"); AddBinding("action:overlay", "toggle overlay");
        foreach (var pad in pads) AddBinding(pad.Id, pad.Name);
    }
    private void AddBinding(string id, string name)
    {
        var row = new Grid { Margin = new(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(190) });
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) };
        if (registrationErrors.TryGetValue(id, out var failure)) { label.Foreground = (Brush)Application.Current.FindResource("Accent"); label.ToolTip = failure; }
        row.Children.Add(label);
        var edit = new Button { Content = chords[id]?.Label ?? "none", Padding = new(8, 7, 8, 7), ToolTip = "change or clear hotkey" };
        Grid.SetColumn(edit, 1); row.Children.Add(edit);
        edit.Click += (_, _) =>
        {
            var picker = new HotkeyWindow(chords[id]) { Owner = this };
            if (picker.ShowDialog() != true) return;
            if (picker.Result is { } chord && chords.Any(p => p.Key != id && p.Value == chord)) { error.Text = "that hotkey is already assigned here."; return; }
            chords[id] = picker.Result; error.Text = ""; BuildBindings();
        };
        bindings.Children.Add(row);
    }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new(0, 0, 0, 7), FontSize = 12 };
    private sealed record DisplayOption(string? Device, string Name) { public override string ToString() => Name; }
    private sealed record CornerOption(OverlayCorner Corner, string Name) { public override string ToString() => Name; }
}

internal sealed class HotkeyWindow : Window
{
    public HotkeyChord? Result { get; private set; }
    internal HotkeyWindow(HotkeyChord? current)
    {
        Title = "hotkey"; Width = 360; Height = 245; ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.FindResource("Bg"); Foreground = (Brush)Application.Current.FindResource("Ink");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(20) }; var modifiers = new StackPanel { Orientation = Orientation.Horizontal };
        var ctrl = new CheckBox { Content = "ctrl", IsChecked = current?.Modifiers.HasFlag(HotkeyModifiers.Control) ?? true, Margin = new(0, 0, 12, 0) };
        var alt = new CheckBox { Content = "alt", IsChecked = current?.Modifiers.HasFlag(HotkeyModifiers.Alt) ?? true, Margin = new(0, 0, 12, 0) };
        var shift = new CheckBox { Content = "shift", IsChecked = current?.Modifiers.HasFlag(HotkeyModifiers.Shift) ?? false };
        modifiers.Children.Add(ctrl); modifiers.Children.Add(alt); modifiers.Children.Add(shift); panel.Children.Add(modifiers);
        var keys = new ComboBox { Margin = new(0, 14, 0, 12) };
        foreach (int key in Enumerable.Range(0x70, 11).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x60, 10)).Concat([0x08, 0x20, 0x21, 0x22, 0x23, 0x24, 0x2d, 0x2e]))
            keys.Items.Add(new KeyOption(key, HotkeyChord.KeyName(key)!));
        keys.SelectedItem = keys.Items.Cast<KeyOption>().FirstOrDefault(k => k.Key == current?.Key) ?? keys.Items[0]; panel.Children.Add(keys);
        var hint = new TextBlock { Text = "include ctrl or alt; f12 is reserved by windows.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Muted") }; panel.Children.Add(hint);
        var actions = new DockPanel { Margin = new(0, 14, 0, 0), LastChildFill = false };
        var clear = new Button { Content = "clear" }; clear.Click += (_, _) => { Result = null; DialogResult = true; }; actions.Children.Add(clear);
        var save = new Button { Content = "save", IsDefault = true }; DockPanel.SetDock(save, Dock.Right);
        save.Click += (_, _) =>
        {
            var chord = new HotkeyChord(((KeyOption)keys.SelectedItem).Key, (ctrl.IsChecked == true ? HotkeyModifiers.Control : 0) | (alt.IsChecked == true ? HotkeyModifiers.Alt : 0) | (shift.IsChecked == true ? HotkeyModifiers.Shift : 0));
            if (!chord.IsValid) { hint.Text = "include ctrl or alt."; return; }
            Result = chord; DialogResult = true;
        };
        actions.Children.Add(save); panel.Children.Add(actions); Content = panel;
    }
    private sealed record KeyOption(int Key, string Name) { public override string ToString() => Name; }
}
