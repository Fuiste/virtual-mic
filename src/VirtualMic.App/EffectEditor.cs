using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VirtualMic.Core.Effects;

namespace VirtualMic.App;

// Only structural edits rebuild controls; parameter edits preserve keyboard focus.
public sealed class EffectEditor : UserControl
{
    private EffectCatalog catalog = null!;
    private readonly List<EffectSlot> slots = [];
    private readonly Dictionary<string, TextBlock> faults = [];
    private readonly StackPanel rows = new();
    private readonly ComboBox available = new() { Width = 156, DisplayMemberPath = "Definition.Name" };
    private readonly Button add = new() { Content = "+", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 6, 0) };
    public event Action? Changed;
    public event Action? PluginsRequested;

    public EffectEditor()
    {
        var root = new DockPanel();
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new TextBlock { Text = "effects", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var plugins = new Button { Content = "plugins", Padding = new Thickness(10, 4, 10, 4) };
        plugins.Click += (_, _) => PluginsRequested?.Invoke();
        DockPanel.SetDock(plugins, Dock.Right); header.Children.Add(plugins);
        DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        DockPanel.SetDock(available, Dock.Right); header.Children.Add(available);
        AutomationProperties.SetName(available, "effect to add");
        AutomationProperties.SetName(add, "add effect to chain");
        add.Click += (_, _) =>
        {
            if (available.SelectedItem is not EffectRegistration effect || slots.Count >= EffectChain.MaximumEffects) return;
            slots.Add(new() { EffectId = effect.Definition.Id, Enabled = true,
                Parameters = effect.Definition.Parameters.ToDictionary(p => p.Id, p => p.DefaultValue) });
            Render(); Changed?.Invoke();
        };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
    }

    public void Load(EffectCatalog source, IReadOnlyList<EffectSlot> chain)
    {
        EffectChain.Validate(chain);
        catalog = source;
        slots.Clear(); slots.AddRange(chain.Select(Clone));
        available.ItemsSource = catalog.Effects.ToArray(); available.SelectedIndex = 0;
        Render();
    }

    public EffectSlot[] Snapshot() => slots.Select(Clone).ToArray();
    private static EffectSlot Clone(EffectSlot slot) => slot with { Parameters = new(slot.Parameters) };
    public void ReportFault(EffectFault fault)
    {
        if (faults.TryGetValue(fault.InstanceId, out var label)) { label.Text = fault.Message; label.Visibility = Visibility.Visible; }
    }
    public void ClearFaults() { foreach (var label in faults.Values) label.Visibility = Visibility.Collapsed; }

    internal void VerifyControls()
    {
        // Exercise the real event wiring using the built-in preview chain.
        UpdateLayout();
        T Control<T>(string name) where T : FrameworkElement => Descendants(this).OfType<T>().First(c => AutomationProperties.GetName(c) == name);
        var gain = Control<Slider>("bass boost boost"); gain.Value = 11;
        var target = Control<ComboBox>("bass boost target"); target.SelectedIndex = 2;
        var toggle = Control<CheckBox>("enable bass boost"); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (slots[0].Parameters["gain"] != 11 || slots[0].Target != EffectTarget.Both || slots[0].Enabled)
            throw new InvalidOperationException("effect parameter, target or bypass controls failed");
        Control<Button>("move bass boost later").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); UpdateLayout();
        if (slots[1].EffectId != "virtualmic.bass") throw new InvalidOperationException("chain reorder failed");
        Control<Button>("remove bass boost").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); UpdateLayout();
        if (slots.Count != 1) throw new InvalidOperationException("remove effect failed");
        Control<Button>("add effect to chain").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (slots.Count != 2 || slots[1].EffectId != "virtualmic.bass") throw new InvalidOperationException("add effect failed");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void Render()
    {
        var previousFaults = faults.Where(x => x.Value.Visibility == Visibility.Visible).ToDictionary(x => x.Key, x => x.Value.Text);
        rows.Children.Clear(); faults.Clear();
        add.IsEnabled = slots.Count < EffectChain.MaximumEffects;
        if (slots.Count == 0) rows.Children.Add(new TextBlock { Text = "no effects", Foreground = Brush("Muted"), Margin = new Thickness(12) });
        foreach (var slot in slots)
        {
            var effect = catalog.Find(slot.EffectId);
            string name = effect?.Definition.Name ?? slot.EffectId;
            var body = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var enabled = new CheckBox { IsChecked = slot.Enabled, VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = effect is null ? "plugin unavailable; saved settings are preserved" : "enable or bypass this effect",
                IsEnabled = effect is not null, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(enabled, "enable " + name);
            enabled.Click += (_, _) => { Replace(slot.InstanceId, s => s with { Enabled = enabled.IsChecked == true }); };
            header.Children.Add(enabled);
            var target = new ComboBox { Width = 90, MinHeight = 28, Padding = new Thickness(6, 3, 20, 3),
                ItemsSource = new[] { "mic", "sounds", "both" }, SelectedIndex = (int)slot.Target,
                ToolTip = "both processes mic and sounds separately before mixing" };
            AutomationProperties.SetName(target, name + " target");
            target.SelectionChanged += (_, _) => Replace(slot.InstanceId, s => s with { Target = (EffectTarget)target.SelectedIndex });
            Grid.SetColumn(target, 1); header.Children.Add(target);
            AddButton(header, 2, "↑", "move " + name + " earlier", slots.IndexOf(slot) > 0, () => Move(slot.InstanceId, -1));
            AddButton(header, 3, "↓", "move " + name + " later", slots.IndexOf(slot) < slots.Count - 1, () => Move(slot.InstanceId, 1));
            AddButton(header, 4, "×", "remove " + name, true, () => { slots.RemoveAll(s => s.InstanceId == slot.InstanceId); Render(); Changed?.Invoke(); });
            body.Children.Add(header);
            if (effect is not null && effect.Definition.Parameters.Count > 0)
            {
                var parameters = new UniformGrid { Columns = Math.Min(2, effect.Definition.Parameters.Count), Margin = new Thickness(-4, 9, -4, 0) };
                var values = effect.Parameters(slot.Parameters);
                foreach (var parameter in effect.Definition.Parameters)
                {
                    var field = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
                    var label = new TextBlock { Foreground = Brush("Muted"), FontSize = 11 };
                    var slider = new Slider { Minimum = parameter.Minimum, Maximum = parameter.Maximum, Value = values[parameter.Id],
                        SmallChange = parameter.Step, LargeChange = parameter.Step * 10, TickFrequency = parameter.Step, IsSnapToTickEnabled = true };
                    AutomationProperties.SetName(slider, name + " " + parameter.Name);
                    void Label() => label.Text = $"{parameter.Name}  {slider.Value:0.##}{(parameter.Unit.Length == 0 ? "" : " " + parameter.Unit)}";
                    Label(); slider.ValueChanged += (_, _) =>
                    {
                        Label(); Replace(slot.InstanceId, s => s with { Parameters = new(s.Parameters) { [parameter.Id] = (float)slider.Value } });
                    };
                    field.Children.Add(label); field.Children.Add(slider); parameters.Children.Add(field);
                }
                body.Children.Add(parameters);
            }
            var fault = new TextBlock { Foreground = Brush("Accent"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            if (effect is null) { fault.Text = "plugin unavailable — bypassed"; fault.Visibility = Visibility.Visible; }
            else if (previousFaults.TryGetValue(slot.InstanceId, out var message)) { fault.Text = message; fault.Visibility = Visibility.Visible; }
            faults[slot.InstanceId] = fault; body.Children.Add(fault);
            rows.Children.Add(new Border { Child = body, Background = Brush("Panel"), BorderBrush = Brush("Line"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 9, 12, 8), Margin = new Thickness(0, 0, 0, 6) });
        }
    }

    private void Replace(string id, Func<EffectSlot, EffectSlot> change)
    { int index = slots.FindIndex(s => s.InstanceId == id); slots[index] = change(slots[index]); Changed?.Invoke(); }
    private void Move(string id, int offset)
    {
        int index = slots.FindIndex(s => s.InstanceId == id); var slot = slots[index];
        slots.RemoveAt(index); slots.Insert(index + offset, slot); Render(); Changed?.Invoke();
    }
    private static void AddButton(Grid grid, int column, string text, string description, bool enabled, Action action)
    {
        var button = new Button { Content = text, ToolTip = description, Width = 28, Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
        AutomationProperties.SetName(button, description); button.Click += (_, _) => action();
        Grid.SetColumn(button, column); grid.Children.Add(button);
    }
    private static Brush Brush(string name) => (Brush)Application.Current.FindResource(name);
}
