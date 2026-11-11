using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screen = System.Windows.Forms.Screen;
using Forms = System.Windows.Forms;

namespace SnowWindows;

internal sealed class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly Action apply;
    private readonly Action exit;
    private readonly Screen targetScreen;
    private readonly Style buttonStyle = (Style)XamlReader.Parse(ButtonXaml);
    private readonly Style toggleStyle = (Style)XamlReader.Parse(ToggleXaml);
    private readonly Style sliderStyle = (Style)XamlReader.Parse(SliderXaml);
    private readonly Style tabsStyle = (Style)XamlReader.Parse(TabsXaml);
    private readonly Style tabStyle = (Style)XamlReader.Parse(TabItemXaml);
    private CheckBox? enabled;
    private TextBlock? status, count;
    private Button? pause;
    private MiniWorld? miniWorld;
    private ScrollViewer? performanceScroll;
    private readonly Dictionary<string, Button> presets = new();
    private bool syncing, advanced;
    private int selectedTab;
    public bool AutoClosed { get; private set; }
    private static readonly Brush Ink = BrushOf("#EAF4FF"), Muted = BrushOf("#93AAC6"), Accent = BrushOf("#3AA0F0"), Surface = BrushOf("#2A3B52"), Line = BrushOf("#3A5272");

    public SettingsWindow(Settings settings, Action apply, Action exit)
    {
        this.settings = settings; this.apply = apply; this.exit = exit;
        targetScreen = Screen.FromPoint(Forms.Cursor.Position);
        Title = "Snow Windows"; WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = true;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13; Foreground = Ink;
        Icon = new BitmapImage(new Uri("pack://application:,,,/SnowWindows;component/Assets/snow-icon-preview.png"));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Deactivated += (_, _) => { if (!advanced && IsVisible) { AutoClosed = true; Close(); } };
        SourceInitialized += (_, _) => PlaceNearTray();
        Loaded += (_, _) => PlaceNearTray();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PlaceNearTray), System.Windows.Threading.DispatcherPriority.Loaded);
        Build();
    }
    private static Brush BrushOf(string value) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); b.Freeze(); return b; }
    private Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Style = buttonStyle, MinHeight = 38, Margin = new Thickness(0), Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); return button;
    }
    private void Build()
    {
        presets.Clear();
        Topmost = !advanced || settings.PanelTopmost;
        Width = advanced ? 560 : 300;
        Height = (advanced ? 720 : 272) + (settings.MiniWorld ? 84 : 0);
        // Once the HWND exists, its actual per-monitor DPI can cap the window to the working area.
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
        {
            double scale = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
            if (scale <= 0) scale = 1;
            Width = Math.Min(Width, Math.Max(260, targetScreen.WorkingArea.Width / scale - 16));
            Height = Math.Min(Height, Math.Max(250, targetScreen.WorkingArea.Height / scale - 16));
        }
        var frame = new Border { Background = BrushOf("#1F2A3A"), BorderBrush = BrushOf("#34507A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14) };
        Content = frame;
        var layers = new Grid(); frame.Child = layers;
        var root = new Grid { Margin = new Thickness(14) }; layers.Children.Add(root);
        if (settings.FrostEdges) layers.Children.Add(CreateFrost());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconBack = new Border { Background = BrushOf("#0F3A5C"), CornerRadius = new CornerRadius(10), Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0), Child = new Image { Source = Icon, Width = 32, Height = 32 } };
        header.Children.Add(iconBack);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        status = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextTrimming = TextTrimming.CharacterEllipsis };
        count = new TextBlock { FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        titles.Children.Add(status); titles.Children.Add(count); Grid.SetColumn(titles, 1); header.Children.Add(titles);
        enabled = MakeToggle("Включить снег и эффекты", settings.EffectiveEnabled, v => { settings.Enabled = v; settings.PauseUntilUtc = null; });
        Grid.SetColumn(enabled, 2); header.Children.Add(enabled);
        var close = ActionButton("×", Close); close.Width = 32; close.MinHeight = 36; close.FontSize = 19; close.Margin = new Thickness(5, 0, 0, 0); close.ToolTip = "Закрыть панель"; AutomationProperties.SetName(close, "Закрыть панель");
        Grid.SetColumn(close, 3); header.Children.Add(close); root.Children.Add(header);

        miniWorld = null;
        if (settings.MiniWorld)
        {
            miniWorld = new MiniWorld(settings);
            Grid.SetRow(miniWorld, 1); root.Children.Add(miniWorld);
        }
        var presetGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition()); presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); presetGrid.ColumnDefinitions.Add(new ColumnDefinition());
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] names = ["Лёгкий", "Метель", "Тихий вечер", "Свой"];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            var button = ActionButton(name, () => { settings.SelectPreset(name); apply(); Build(); });
            button.Margin = new Thickness(0, 0, 0, i < 2 ? 8 : 0); button.ToolTip = name == "Свой" ? "Ваши сохранённые настройки снега" : $"Пресет: {name}";
            Grid.SetRow(button, i / 2); Grid.SetColumn(button, (i % 2) * 2); presetGrid.Children.Add(button); presets[name] = button;
        }
        Grid.SetRow(presetGrid, 2); root.Children.Add(presetGrid);
        var details = ActionButton(advanced ? "Скрыть подробные настройки  ∧" : "Дополнительно  ∨", () => { advanced = !advanced; Build(); Dispatcher.BeginInvoke(new Action(PlaceNearTray), System.Windows.Threading.DispatcherPriority.Loaded); });
        Grid.SetRow(details, 3); root.Children.Add(details);

        if (advanced)
        {
            var extra = new Grid { Margin = new Thickness(0, 10, 0, 8) };
            extra.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            extra.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            extra.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pinRow = new Grid { Margin = new Thickness(0, 0, 0, 8), MinHeight = 30 };
            pinRow.ColumnDefinitions.Add(new ColumnDefinition()); pinRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pinRow.Children.Add(new TextBlock { Text = "Поверх всех окон", Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
            var pin = MakeToggle("Поверх всех окон", settings.PanelTopmost, v => { settings.PanelTopmost = v; Topmost = v; });
            Grid.SetColumn(pin, 1); pinRow.Children.Add(pin); extra.Children.Add(pinRow);
            var tabs = new TabControl { Style = tabsStyle, ItemContainerStyle = tabStyle, SelectedIndex = Math.Clamp(selectedTab, 0, 3) };
            AddTab(tabs, "Снег", BuildSnow); AddTab(tabs, "Сугробы", BuildBanks); AddTab(tabs, "Машинка", BuildPlow); AddTab(tabs, "Плавность", BuildPerformance);
            tabs.SelectedIndex = Math.Clamp(selectedTab, 0, 3);
            tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, tabs) && tabs.SelectedIndex >= 0) selectedTab = tabs.SelectedIndex; };
            Grid.SetRow(tabs, 1); extra.Children.Add(tabs);
            var resetRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            resetRow.ColumnDefinitions.Add(new ColumnDefinition()); resetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); resetRow.ColumnDefinitions.Add(new ColumnDefinition());
            var resetTab = ActionButton("Сбросить вкладку", () => { if (Confirm("Сбросить настройки текущей вкладки?")) { settings.ResetTab(selectedTab); apply(); Build(); PlaceNearTray(); } });
            var resetAll = ActionButton("Сбросить всё", () => { if (Confirm("Сбросить все настройки приложения?")) { settings.ResetAll(); apply(); Build(); PlaceNearTray(); } });
            resetRow.Children.Add(resetTab); Grid.SetColumn(resetAll, 2); resetRow.Children.Add(resetAll);
            Grid.SetRow(resetRow, 2); extra.Children.Add(resetRow);
            Grid.SetRow(extra, 4); root.Children.Add(extra);
        }
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); footer.ColumnDefinitions.Add(new ColumnDefinition());
        pause = ActionButton("Пауза на 1 час", () => {
            if (settings.IsPaused) settings.PauseUntilUtc = null;
            else if (settings.Enabled) settings.PauseUntilUtc = DateTimeOffset.UtcNow.AddHours(1);
            apply(); SyncEnabled();
        });
        var quit = ActionButton("Выход", exit); footer.Children.Add(pause); Grid.SetColumn(quit, 2); footer.Children.Add(quit);
        Grid.SetRow(footer, 5); root.Children.Add(footer);
        SyncEnabled();
    }
    private void RebuildDecorations()
    {
        double offset = performanceScroll?.VerticalOffset ?? 0;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            Build(); PlaceNearTray();
            Dispatcher.BeginInvoke(new Action(() => performanceScroll?.ScrollToVerticalOffset(offset)), System.Windows.Threading.DispatcherPriority.Loaded);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
    private static Grid CreateFrost()
    {
        var layer = new Grid { IsHitTestVisible = false, Opacity = 0.35 };
        var geometry = Geometry.Parse("M 0,37 C 5,25 15,16 33,3 M 7,27 L 3,20 M 12,21 L 9,12 M 18,16 L 19,6 M 23,12 L 31,13 M 6,29 L 14,30 M 12,22 L 21,23 M 17,16 L 26,18 M 1,43 C 4,36 10,32 16,29");
        geometry.Freeze();
        for (int i = 0; i < 4; i++)
        {
            var branch = new System.Windows.Shapes.Path {
                Data = geometry, Stroke = BrushOf("#C5EAFF"), StrokeThickness = 1.25,
                Width = 34, Height = 44, Stretch = Stretch.Fill,
                HorizontalAlignment = (i % 2 == 0) ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = (i < 2) ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new ScaleTransform(i % 2 == 0 ? 1 : -1, i < 2 ? 1 : -1)
            };
            layer.Children.Add(branch);
        }
        return layer;
    }
    private bool Confirm(string text) => System.Windows.MessageBox.Show(this, text, "Snow Windows", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void PlaceNearTray()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == IntPtr.Zero) return;
        double scale = Native.GetDpiForWindow(hwnd) / 96.0; if (scale <= 0) scale = 1;
        var w = targetScreen.WorkingArea; var b = targetScreen.Bounds;
        Width = Math.Min(advanced ? 560 : 300, Math.Max(1, w.Width / scale - 16));
        Height = Math.Min((advanced ? 720 : 272) + (settings.MiniWorld ? 84 : 0), Math.Max(1, w.Height / scale - 16));
        int width = (int)Math.Ceiling(Width * scale), height = (int)Math.Ceiling(Height * scale), margin = (int)Math.Ceiling(8 * scale);
        int x = w.Left > b.Left ? w.Left + margin : Math.Max(w.Left, w.Right - width - margin);
        int y = w.Top > b.Top ? w.Top + margin : Math.Max(w.Top, w.Bottom - height - margin);
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }
    private CheckBox MakeToggle(string label, bool value, Action<bool> set)
    {
        var check = new CheckBox { Style = toggleStyle, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(check, label);
        check.Checked += (_, _) => { if (!syncing) { set(true); apply(); SyncEnabled(); } };
        check.Unchecked += (_, _) => { if (!syncing) { set(false); apply(); SyncEnabled(); } };
        return check;
    }
    public void SyncEnabled()
    {
        syncing = true;
        if (enabled is not null) enabled.IsChecked = settings.EffectiveEnabled;
        if (status is not null) status.Text = settings.IsPaused ? "Снег на паузе" : !settings.EffectiveEnabled ? "Снег выключен" : settings.FallingSnow ? "Снег включён" : "Эффекты включены";
        if (count is not null) count.Text = settings.IsPaused ? $"До {settings.PauseUntilUtc!.Value.ToLocalTime():HH:mm}" : settings.FallingSnow ? SnowCount(settings.Density) : "Снегопад выключен";
        if (pause is not null) { pause.Content = settings.IsPaused ? "Возобновить" : "Пауза на 1 час"; pause.IsEnabled = settings.Enabled || settings.IsPaused; }
        foreach (var item in presets)
        {
            bool active = item.Key == settings.SelectedPreset;
            item.Value.Background = active ? Accent : Surface; item.Value.BorderBrush = active ? Accent : Line;
            item.Value.Foreground = active ? BrushOf("#102235") : Ink;
            item.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetHelpText(item.Value, active ? "Активный пресет" : "Выбрать пресет");
        }
        syncing = false;
        miniWorld?.Refresh();
    }
    private static string SnowCount(int n)
    {
        int lastTwo = n % 100, last = n % 10;
        string word = lastTwo is >= 11 and <= 14 ? "снежинок" : last == 1 ? "снежинка" : last is >= 2 and <= 4 ? "снежинки" : "снежинок";
        return $"{n} {word}";
    }
    private void AddTab(TabControl tabs, string title, Action<StackPanel> build)
    {
        var body = new StackPanel { Margin = new Thickness(2, 12, 2, 8) }; build(body);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
        if (title == "Плавность") performanceScroll = scroll;
        tabs.Items.Add(new TabItem { Header = title, Content = scroll });
    }
    private void AddToggle(Panel panel, string title, string description, bool value, Action<bool> set)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4), MinHeight = 42 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title, Foreground = Ink, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (description.Length > 0) label.Children.Add(new TextBlock { Text = description, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(label); var check = MakeToggle(title, value, set); Grid.SetColumn(check, 1); row.Children.Add(check); panel.Children.Add(row);
    }
    private void AddSlider(Panel panel, string label, int min, int max, int value, Action<int> set, string unit, bool snow = false)
    {
        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = label, Foreground = Ink, FontWeight = FontWeights.SemiBold });
        var amount = new TextBlock { Text = $"{value}{unit}", Foreground = Accent, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(amount, 1); header.Children.Add(amount);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Style = sliderStyle, MinHeight = 30 };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) => { int v = (int)Math.Round(slider.Value); amount.Text = $"{v}{unit}"; set(v); if (snow) settings.CaptureCustom(); apply(); SyncEnabled(); };
        row.Children.Add(header); row.Children.Add(slider); panel.Children.Add(row);
    }
    private void BuildSnow(StackPanel p)
    {
        AddToggle(p, "Падающий снег", "На видимой части рабочего стола", settings.FallingSnow, v => settings.FallingSnow = v);
        AddToggle(p, "Реакция на курсор", "Снежинки разлетаются возле мыши", settings.MouseInteraction, v => settings.MouseInteraction = v);
        AddToggle(p, "Таяние у окон", "Снежинки исчезают у края окна", settings.WindowInteraction, v => settings.WindowInteraction = v);
        AddSlider(p, "Количество", 0, 350, settings.Density, v => settings.Density = v, " шт.", true);
        AddSlider(p, "Скорость", 10, 200, settings.Speed, v => settings.Speed = v, "%", true);
        AddSlider(p, "Размер", 50, 200, settings.FlakeSize, v => settings.FlakeSize = v, "%", true);
        AddSlider(p, "Видимость снежинок", 10, 100, settings.Opacity, v => settings.Opacity = v, "%", true);
        AddSlider(p, "Ветер", -30, 30, settings.Wind, v => settings.Wind = v, " px/с", true);
    }
    private void BuildBanks(StackPanel p)
    {
        AddToggle(p, "Сугробы на рабочем столе", "Накопление снега внизу", settings.Snowdrifts, v => settings.Snowdrifts = v);
        AddToggle(p, "Сметать курсором", "Проведи мышью вдоль сугроба", settings.SweepDrifts, v => settings.SweepDrifts = v);
        AddToggle(p, "Снеговик", "Иногда появляется на сугробе", settings.Snowman, v => settings.Snowman = v);
        AddSlider(p, "Максимальная высота", 5, 60, settings.DriftHeight, v => settings.DriftHeight = v, " px");
        AddSlider(p, "Видимость сугроба", 10, 100, settings.DriftOpacity, v => settings.DriftOpacity = v, "%");
    }
    private void BuildPlow(StackPanel p)
    {
        AddToggle(p, "Машинка-снегоуборщик", "Работает, когда включены сугробы", settings.Snowplow, v => settings.Snowplow = v);
        AddToggle(p, "Разлетающийся снег", "Брызги при уборке сугроба", settings.PlowSpray, v => settings.PlowSpray = v);
        AddToggle(p, "Машинка иногда застревает", "Перед большим сугробом ненадолго сдаёт назад", settings.PlowGetsStuck, v => settings.PlowGetsStuck = v);
        AddSlider(p, "Скорость машинки", 20, 160, settings.PlowSpeed, v => settings.PlowSpeed = v, " px/с");
        AddSlider(p, "Пауза между проездами", 5, 120, settings.PlowInterval, v => settings.PlowInterval = v, " с");
        p.Children.Add(new TextBlock { Text = "Если путь закрыт окном, машинка подождёт.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
    }
    private void BuildPerformance(StackPanel p)
    {
        AddToggle(p, "Экономичный режим", "Ограничить снегопад до 30 кадров/с", settings.EcoMode, v => settings.EcoMode = v);
        AddToggle(p, "Анимации интерфейса", "Учитывает настройки Windows", settings.UiAnimations, v => settings.UiAnimations = v);
        AddToggle(p, "Зимний мини-мирок", "День и ночь по времени компьютера", settings.MiniWorld, v => { settings.MiniWorld = v; RebuildDecorations(); });
        AddToggle(p, "Морозные узоры", "Только по краям панели", settings.FrostEdges, v => { settings.FrostEdges = v; RebuildDecorations(); });
        AddToggle(p, "Заставка при запуске", "При каждом ручном запуске; без автозапуска", settings.StartupAnimation, v => settings.StartupAnimation = v);
        var rates = string.Join("  ·  ", Screen.AllScreens.Select((screen, index) => { int rate = Native.DisplayRefreshRate(screen.DeviceName); return rate > 0 ? $"Монитор {index + 1}: {rate} Гц" : $"Монитор {index + 1}: частота неизвестна"; }));
        p.Children.Add(new TextBlock { Text = rates, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        p.Children.Add(new TextBlock { Text = "При развёрнутом или полноэкранном активном окне снег на этом мониторе приостанавливается.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
    }
    private const string ButtonXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Button}">
 <Setter Property="Background" Value="#2A3B52"/><Setter Property="BorderBrush" Value="#3A5272"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Foreground" Value="#EAF4FF"/><Setter Property="Padding" Value="9,7"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Button}">
 <Border x:Name="B" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/></Border>
 <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="Opacity" Value="0.82"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#C0E3FF"/><Setter TargetName="B" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.48"/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>
""";
    private const string ToggleXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type CheckBox}">
 <Setter Property="Width" Value="46"/><Setter Property="Height" Value="26"/><Setter Property="Cursor" Value="Hand"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type CheckBox}">
 <Border x:Name="T" Background="#40536A" BorderBrush="#7D95AC" BorderThickness="1" CornerRadius="13"><Ellipse x:Name="K" Fill="#EAF4FF" Width="18" Height="18" HorizontalAlignment="Left" Margin="3,0,3,0"/></Border>
 <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="T" Property="Background" Value="#3AA0F0"/><Setter TargetName="T" Property="BorderBrush" Value="#3AA0F0"/><Setter TargetName="K" Property="HorizontalAlignment" Value="Right"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="T" Property="BorderBrush" Value="#EAF4FF"/><Setter TargetName="T" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>
""";
    private const string TabsXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type TabControl}">
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type TabControl}"><Grid ClipToBounds="True"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions><Border Background="#26384D" CornerRadius="10" Padding="3"><TabPanel x:Name="HeaderPanel" IsItemsHost="True"/></Border><ContentPresenter x:Name="PART_SelectedContentHost" Grid.Row="1" ContentSource="SelectedContent"/></Grid></ControlTemplate></Setter.Value></Setter>
</Style>
""";
    private const string TabItemXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type TabItem}">
 <Setter Property="Foreground" Value="#B5C8DB"/><Setter Property="Padding" Value="10,10"/><Setter Property="FontSize" Value="12"/><Setter Property="Cursor" Value="Hand"/>
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type TabItem}"><Border x:Name="B" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="8" Padding="{TemplateBinding Padding}" MinHeight="40"><ContentPresenter ContentSource="Header" TextElement.Foreground="{TemplateBinding Foreground}" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="B" Property="Background" Value="#3AA0F0"/><Setter Property="Foreground" Value="#102235"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="B" Property="BorderBrush" Value="#EAF4FF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
""";
    private const string SliderXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Slider}">
 <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type Slider}">
 <Grid><Track x:Name="PART_Track" VerticalAlignment="Center">
  <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static Slider.DecreaseLarge}"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="#3AA0F0" Height="4" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
  <Track.Thumb><Thumb Width="16" Height="16"><Thumb.Template><ControlTemplate TargetType="{x:Type Thumb}"><Ellipse Fill="#3AA0F0" Stroke="#D9EEFF" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
  <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static Slider.IncreaseLarge}"><RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="#526B83" Height="4" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
 </Track></Grid>
 </ControlTemplate></Setter.Value></Setter>
</Style>
""";
}
