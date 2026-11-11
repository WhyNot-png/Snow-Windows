using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SnowWindows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new System.Threading.Mutex(true, "Local\\SnowWindows.SingleInstance", out bool first);
        if (!first) { System.Windows.MessageBox.Show("Snow Windows уже запущен. Откройте его через значок возле часов.", "Snow Windows"); return; }
        bool autostart = args.Any(x => string.Equals(x, "--autostart", StringComparison.OrdinalIgnoreCase));
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new Controller(app, autostart);
        app.Run(); controller.Dispose();
    }
}
internal sealed class Controller : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly Settings settings = Settings.Load();
    private readonly Forms.NotifyIcon tray;
    private readonly List<SnowOverlay> overlays = [];
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer pauseTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string monitorSignature = "";
    private SettingsWindow? settingsWindow;
    private StartupWindow? startupWindow;
    private bool stopping, savePending;
    private long lastAutoClose = -10000, lastTrayClick = -10000;
    private readonly Forms.ToolStripMenuItem toggleItem;
    public Controller(System.Windows.Application app, bool autostart)
    {
        this.app = app;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Настройки…", null, (_, _) => ShowSettings());
        toggleItem = new Forms.ToolStripMenuItem();
        toggleItem.Click += (_, _) => { settings.Enabled = !settings.EffectiveEnabled; settings.PauseUntilUtc = null; Apply(); };
        menu.Items.Add(toggleItem); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());
        Drawing.Icon? appIcon = null;
        try { if (Environment.ProcessPath is { } path) appIcon = Drawing.Icon.ExtractAssociatedIcon(path); } catch { }
        tray = new Forms.NotifyIcon { Text = "Snow Windows", Icon = appIcon ?? Drawing.SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => {
            if (e.Button != Forms.MouseButtons.Left) return;
            long now = Environment.TickCount64;
            if (now - lastTrayClick < 400) return; // A double-click must not open and immediately close.
            lastTrayClick = now;
            if (startupWindow is not null) { startupWindow.Close(); return; }
            if (settingsWindow is not null) { settingsWindow.Close(); return; }
            // Compact flyout may have closed on deactivation before the tray click arrives.
            if (now - lastAutoClose < 400) return;
            ShowSettings();
        };
        displayTimer.Tick += (_, _) => RefreshDisplays(); displayTimer.Start();
        pauseTimer.Tick += (_, _) => {
            if (settings.PauseUntilUtc is { } until && until <= DateTimeOffset.UtcNow)
            { settings.PauseUntilUtc = null; Apply(); }
        }; pauseTimer.Start();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
        bool fullscreen = Forms.Screen.AllScreens.Any(screen => Native.IsFullscreen(Native.GetForegroundWindow(), screen.Bounds));
        RefreshDisplays(); Apply();
        if (!autostart && !fullscreen && settings.StartupAnimation)
        {
            startupWindow = new StartupWindow(settings.UiAnimations && Native.AllowInterfaceAnimations());
            startupWindow.Closed += (_, _) => { startupWindow = null; if (!stopping) ShowSettings(); };
            startupWindow.Show();
        }
        else if (!autostart && !fullscreen) ShowSettings();
    }
    private void RefreshDisplays()
    {
        var screens = Forms.Screen.AllScreens;
        var signature = string.Join(";", screens.Select(s => $"{s.DeviceName}:{s.Bounds}:{s.WorkingArea}"));
        if (signature == monitorSignature) { foreach (var o in overlays) o.UpdateRefreshRate(); return; }
        monitorSignature = signature;
        foreach (var o in overlays) o.Close(); overlays.Clear();
        foreach (var screen in screens) { var o = new SnowOverlay(settings, screen); overlays.Add(o); o.Show(); o.RefreshState(); }
    }
    private void ShowSettings()
    {
        if (startupWindow is not null) { startupWindow.Close(); return; }
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        var window = new SettingsWindow(settings, Apply, Exit); settingsWindow = window;
        window.Closed += (_, _) => { if (window.AutoClosed) lastAutoClose = Environment.TickCount64; if (ReferenceEquals(settingsWindow, window)) settingsWindow = null; };
        window.Show(); window.Activate();
    }
    private void Apply()
    {
        toggleItem.Text = settings.EffectiveEnabled ? "Выключить снег" : "Включить снег";
        settingsWindow?.SyncEnabled();
        foreach (var o in overlays) o.RefreshState(forceRedraw: true);
        savePending = true; saveTimer.Stop(); saveTimer.Start();
    }
    private void SaveNow()
    {
        if (!savePending) return;
        try { settings.Save(); savePending = false; }
        catch (Exception ex) { System.Windows.MessageBox.Show("Не удалось сохранить настройки: " + ex.Message, "Snow Windows"); }
    }
    private void Exit() { stopping = true; SaveNow(); app.Shutdown(); }
    public void Dispose()
    {
        stopping = true; displayTimer.Stop(); pauseTimer.Stop(); saveTimer.Stop(); SaveNow();
        foreach (var o in overlays) o.Close(); tray.Visible = false; tray.Dispose();
    }
}
