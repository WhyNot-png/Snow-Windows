using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace SnowWindows;

internal sealed class SnowOverlay : Window
{
    private readonly Settings settings;
    private readonly Screen screen;
    private readonly SnowCanvas canvas;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private IntPtr handle;
    private bool disposed;
    public string DeviceName => screen.DeviceName;
    public void UpdateRefreshRate() => canvas.SetRefreshRate(Native.DisplayRefreshRate(screen.DeviceName));
    public SnowOverlay(Settings settings, Screen screen)
    {
        this.settings = settings; this.screen = screen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Focusable = false; IsHitTestVisible = false;
        canvas = new SnowCanvas(settings, screen);
        UpdateRefreshRate();
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.MakeClickThrough(handle);
            var b = screen.Bounds;
            Native.SetWindowPos(handle, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        };
        timer.Tick += (_, _) => RefreshState(); timer.Start();
        Closed += (_, _) => { disposed = true; timer.Stop(); canvas.Stop(); };
    }
    public void RefreshState(bool forceRedraw = false)
    {
        if (disposed || handle == IntPtr.Zero) return;
        bool active = settings.EffectiveEnabled && (settings.FallingSnow || settings.Snowdrifts) && !Native.ShouldPauseForForeground(Native.GetForegroundWindow(), screen.Bounds);
        if (canvas.Active != active || forceRedraw)
        {
            canvas.Active = active;
            canvas.InvalidateVisual();
            // Hide the entire transparent surface during pause, not only its pixels.
            if (!active && IsVisible) Hide();
            else if (active && !IsVisible)
            {
                Show();
                var b = screen.Bounds;
                Native.SetWindowPos(handle, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
        }
    }
}

internal sealed class SnowCanvas : FrameworkElement
{
    private sealed class Flake
    {
        public double X, Y, Size, Factor, Phase;
        public IntPtr LandedWindow;
        public double WindowFraction, MeltTime, Visibility;
    }
    private sealed class SprayParticle
    {
        public double X, Y, Vx, Vy, Age, Life, Radius;
    }
    private readonly Settings settings;
    private readonly Screen screen;
    private readonly Random random = new();
    private readonly List<Flake> flakes = [];
    private readonly List<SprayParticle> spray = [];
    private double sprayCarry;
    private double snowmanX = double.NaN, snowmanAge, snowmanDelay = 8, snowmanGround;
    private const double SnowmanWidth = 42, SnowmanHeight = 58;
    private List<Native.WindowInfo> windows = [];
    private Geometry desktopClip = Geometry.Empty;
    private double[] drifts = [];
    private readonly List<Rect> occupiedRects = [];
    private Rect workRect;
    private Geometry driftGeometry = Geometry.Empty;
    private bool driftDirty = true;
    private int previousDriftLimit = -1, previousOpacity = -1, previousDriftOpacity = -1;
    private readonly SolidColorBrush[] snowBrushes = new SolidColorBrush[17];
    private Brush? driftBrush;
    private Point lastCursor;
    private bool hasLastCursor;
    private const double PlowWidth = 112, PlowHeight = 56, PlowBladeX = 105;
    private static readonly ImageSource PlowImage = LoadImage("snowplow.png");
    private static readonly ImageSource SnowmanImage = LoadImage("snowman.png");
    private double plowX = double.NaN, plowWait;
    private readonly PlowStuckAnimation plowStuck = new();
    private bool stuckSelectedPass, stuckTriggeredPass;
    private double lastStuckTime = -PlowStuckAnimation.CooldownSeconds;
    private double workLeft, workRight, groundY;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private double lastTime;
    private double nextFrameAt;
    private int refreshRate;
    private double lastWindowScan = -10;
    public bool Active { get; set; } = true;
    public SnowCanvas(Settings settings, Screen screen)
    {
        this.settings = settings; this.screen = screen;
        CompositionTarget.Rendering += RenderFrame;
    }
    public void Stop() => CompositionTarget.Rendering -= RenderFrame;
    public void SetRefreshRate(int rate)
    {
        if (refreshRate == rate) return;
        refreshRate = rate;
        nextFrameAt = 0;
    }
    private void Reset(Flake f)
    {
        f.X = random.NextDouble() * ActualWidth; f.Y = -4 - random.NextDouble() * 24;
        f.LandedWindow = IntPtr.Zero; f.MeltTime = 0; f.Visibility = 0;
    }
    private void RenderFrame(object? sender, EventArgs e)
    {
        double now = stopwatch.Elapsed.TotalSeconds;
        if (!Active || (!settings.FallingSnow && !settings.Snowdrifts) || ActualWidth <= 0 || ActualHeight <= 0)
        {
            lastTime = now;
            nextFrameAt = now;
            hasLastCursor = false;
            return;
        }
        // The compositor supplies the frame callbacks. Automatic mode uses
        // the monitor's current refresh as a target, not a guaranteed FPS.
        // An absolute deadline avoids accumulating rounding errors on e.g. a
        // 60 Hz monitor when callbacks arrive at a 144 Hz cadence.
        bool animated = settings.FallingSnow || (settings.Snowdrifts && (settings.Snowplow || settings.Snowman));
        double interval = !animated ? 0.10 : settings.EcoMode ? 1.0 / 30 : refreshRate > 0 ? 1.0 / refreshRate : 0;
        if (interval > 0 && now + 0.0001 < nextFrameAt) return;
        double dt = Math.Clamp(now - lastTime, 0, 0.08); lastTime = now;
        if (interval == 0) nextFrameAt = now;
        else if (nextFrameAt < now - interval) nextFrameAt = now + interval;
        else nextFrameAt += interval;
        // Use a cached window list both for clipping to the visible desktop
        // and for collisions; never paint on top of detected application windows.
        if (now - lastWindowScan >= 0.10)
        {
            windows = Native.VisibleWindowBounds();
            UpdateDesktopClip();
            lastWindowScan = now;
        }
        if (desktopClip.IsEmpty()) { InvalidateVisual(); return; }
        UpdateGround();
        UpdateSnowplow(dt);
        UpdateSpray(dt);
        UpdateSnowman(dt);
        var b = screen.Bounds;
        double sx = ActualWidth / b.Width, sy = ActualHeight / b.Height;
        Native.POINT cursor = default;
        bool cursorOk = (settings.MouseInteraction || settings.SweepDrifts) && Native.GetCursorPos(out cursor);
        var mouse = new Point((cursor.X - b.Left) * sx, (cursor.Y - b.Top) * sy);
        if (cursorOk && settings.Snowdrifts && settings.SweepDrifts && hasLastCursor)
            SweepSnow(lastCursor, mouse);
        hasLastCursor = cursorOk;
        if (cursorOk) lastCursor = mouse;
        if (settings.FallingSnow)
        {
            while (flakes.Count < settings.Density)
                flakes.Add(new Flake { X = random.NextDouble() * ActualWidth, Y = random.NextDouble() * ActualHeight, Size = 0.8 + random.NextDouble() * 2.2, Factor = 0.5 + random.NextDouble(), Phase = random.NextDouble() * 6.28 });
            if (flakes.Count > settings.Density) flakes.RemoveRange(settings.Density, flakes.Count - settings.Density);
            bool mouseOk = settings.MouseInteraction && cursorOk;
            double mouseX = mouse.X, mouseY = mouse.Y;
            foreach (var f in flakes)
            {
                if (f.LandedWindow != IntPtr.Zero)
                {
                    var found = windows.Find(w => w.Handle == f.LandedWindow);
                    if (!settings.WindowInteraction || found.Handle == IntPtr.Zero) { Reset(f); continue; }
                    var r = found.Bounds;
                    f.X = (r.Left + f.WindowFraction * (r.Right - r.Left) - b.Left) * sx;
                    f.Y = (r.Top - b.Top) * sy - Math.Max(1, f.Size * 0.7);
                    f.MeltTime += dt;
                    UpdateFlakeVisibility(f, dt);
                    if (f.MeltTime >= 0.8) Reset(f);
                    continue;
                }
                double oldY = f.Y;
                f.Y += dt * (62 * settings.Speed / 100.0) * f.Factor;
                f.X += dt * (settings.Wind + Math.Sin(now * 0.8 + f.Phase) * 7);
                if (mouseOk)
                {
                    double dx = f.X - mouseX, dy = f.Y - mouseY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 0.01 && dist < 75)
                    {
                        double force = (75 - dist) / 75 * 85 * dt;
                        f.X += dx / dist * force; f.Y += dy / dist * force;
                    }
                }
                if (settings.WindowInteraction && f.Y >= oldY)
                {
                    foreach (var w in windows)
                    {
                        var r = w.Bounds;
                        double top = (r.Top - b.Top) * sy, left = (r.Left - b.Left) * sx, right = (r.Right - b.Left) * sx;
                        if (f.X >= left && f.X <= right && oldY <= top && f.Y >= top)
                        {
                            f.LandedWindow = w.Handle; f.WindowFraction = Math.Clamp((f.X - left) / (right - left), 0, 1);
                            f.Y = top - Math.Max(1, f.Size * 0.7); f.MeltTime = 0; break;
                        }
                    }
                }
                if (f.X < -5) f.X = ActualWidth; else if (f.X > ActualWidth + 5) f.X = 0;
                if (f.LandedWindow == IntPtr.Zero && f.X >= workLeft && f.X <= workRight && f.Y >= groundY - CurrentHeight(f.X))
                {
                    if (settings.Snowdrifts && IsDesktopVisible(f.X, groundY - 0.5)) AddSnow(f.X);
                    Reset(f);
                    continue;
                }
                if (f.Y > ActualHeight + 5) { Reset(f); continue; }
                UpdateFlakeVisibility(f, dt);
            }
        }
        InvalidateVisual();
    }
    private static ImageSource LoadImage(string name)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/SnowWindows;component/Assets/{name}"));
        image.Freeze();
        return image;
    }
    private void UpdateSnowplow(double dt)
    {
        if (!settings.Snowplow || !settings.Snowdrifts || workRight <= workLeft)
        {
            plowX = double.NaN; plowWait = 0; plowStuck.Cancel();
            return;
        }
        if (double.IsNaN(plowX)) { plowX = workLeft - PlowWidth; BeginPlowPass(); }
        if (!settings.PlowGetsStuck && plowStuck.Active) plowStuck.Cancel();
        if (plowWait > 0) { plowWait = Math.Max(0, plowWait - dt); return; }
        if (plowStuck.Active)
        {
            double front = plowStuck.Origin + PlowBladeX + 36;
            bool blocked = false;
            plowX = plowStuck.Step(dt, x => {
                bool clear = front >= workLeft && front < workRight && IsDesktopVisible(front, groundY - 2)
                    && IsDesktopVisible(x + PlowBladeX, groundY - 2);
                if (!clear) blocked = true;
                return clear;
            });
            // Never let the decorative animation trap the real plow. On a new
            // obstruction or geometry change, cancel it in place; regular motion
            // resumes and its usual window avoidance decides whether to wait.
            if (blocked) plowStuck.Cancel();
            return;
        }
        double nextX = plowX + settings.PlowSpeed * dt;
        double bladeX = nextX + PlowBladeX;
        // Respect desktop-only mode: stop before the blade enters a covered
        // portion, rather than visibly travelling on top of a window.
        if (bladeX >= workLeft && bladeX < workRight && !IsDesktopVisible(bladeX, groundY - 2)) return;
        // Inspect uncleared snow ahead of the blade, not the already-cleaned cells.
        double probe = bladeX + 36;
        if (settings.PlowGetsStuck && stuckSelectedPass && !stuckTriggeredPass
            && bladeX >= workLeft && probe < workRight && IsDesktopVisible(probe, groundY - 2)
            && PlowStuckAnimation.CanStart(true, true, false,
                stopwatch.Elapsed.TotalSeconds - lastStuckTime, CurrentHeight(probe), settings.DriftHeight)
            && PlowStuckAnimation.HasRetreatRoom(plowX + PlowBladeX, workLeft, x => IsDesktopVisible(x, groundY - 2)))
        {
            stuckTriggeredPass = true; lastStuckTime = stopwatch.Elapsed.TotalSeconds;
            plowStuck.Start(plowX); return;
        }
        plowX = nextX;
        if (bladeX >= workLeft && bladeX <= workRight && drifts.Length > 0)
        {
            int center = Math.Clamp((int)((bladeX - workLeft) / (workRight - workLeft) * (drifts.Length - 1)), 0, drifts.Length - 1);
            double removed = 0, highest = 0;
            for (int i = Math.Max(0, center - 2); i <= Math.Min(drifts.Length - 1, center + 2); i++)
            {
                double previous = drifts[i];
                highest = Math.Max(highest, previous);
                double newHeight = Math.Max(0, previous - settings.DriftHeight * dt * 12);
                removed += previous - newHeight;
                if (newHeight != previous) { drifts[i] = newHeight; driftDirty = true; }
            }
            if (settings.PlowSpray && removed > 0.01) EmitSpray(bladeX, groundY - highest, dt);
        }
        if (plowX > workRight + 4)
        {
            plowX = workLeft - PlowWidth;
            plowWait = settings.PlowInterval;
            BeginPlowPass();
        }
    }
    private void BeginPlowPass()
    {
        stuckSelectedPass = random.Next(4) == 0;
        stuckTriggeredPass = false;
    }
    private void EmitSpray(double x, double y, double dt)
    {
        // A small fixed cap keeps this decoration inexpensive on high-Hz screens.
        sprayCarry += dt * 14;
        while (sprayCarry >= 1 && spray.Count < 28)
        {
            sprayCarry -= 1;
            spray.Add(new SprayParticle {
                X = x + random.NextDouble() * 5, Y = y - 1,
                Vx = 10 + random.NextDouble() * 35, Vy = -25 - random.NextDouble() * 50,
                Life = 0.45 + random.NextDouble() * 0.35, Radius = 1 + random.NextDouble() * 1.6
            });
        }
        if (spray.Count >= 28) sprayCarry = 0;
    }
    private void UpdateSpray(double dt)
    {
        if (!settings.PlowSpray || !settings.Snowplow || !settings.Snowdrifts)
        {
            spray.Clear(); sprayCarry = 0; return;
        }
        for (int i = spray.Count - 1; i >= 0; i--)
        {
            var p = spray[i]; p.Age += dt;
            p.X += p.Vx * dt; p.Y += p.Vy * dt; p.Vy += 105 * dt;
            if (p.Age >= p.Life || p.Y >= groundY) spray.RemoveAt(i);
        }
    }
    private void DrawSpray(DrawingContext dc)
    {
        if (!settings.PlowSpray || !settings.Snowplow || !settings.Snowdrifts) return;
        foreach (var p in spray)
        {
            int index = Math.Clamp((int)Math.Round(16 * (1 - p.Age / p.Life)), 0, 16);
            if (index > 0) dc.DrawEllipse(snowBrushes[index], null, new Point(p.X, p.Y), p.Radius, p.Radius);
        }
    }
    private void UpdateSnowman(double dt)
    {
        if (!settings.Snowman || !settings.Snowdrifts || workRight - workLeft < SnowmanWidth + 20)
        {
            snowmanX = double.NaN; snowmanAge = 0; snowmanDelay = 8; return;
        }
        if (double.IsNaN(snowmanX))
        {
            snowmanDelay -= dt;
            if (snowmanDelay > 0) return;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                double x = workLeft + SnowmanWidth / 2 + 8 + random.NextDouble() * (workRight - workLeft - SnowmanWidth - 16);
                double height = CurrentHeight(x);
                double y = groundY - height + 3;
                if (height < 3 || !SnowmanAreaVisible(x, y)) continue;
                if (settings.Snowplow && !double.IsNaN(plowX) && plowWait <= 0 && Math.Abs(plowX + PlowBladeX - x) < 90) continue;
                snowmanX = x; snowmanGround = y; snowmanAge = 0; return;
            }
            snowmanDelay = 5; return;
        }
        snowmanAge += dt;
        double desired = groundY - CurrentHeight(snowmanX) + 3;
        snowmanGround += (desired - snowmanGround) * Math.Min(1, dt * 4);
        bool plowReached = settings.Snowplow && !double.IsNaN(plowX) && plowWait <= 0 && Math.Abs(plowX + PlowBladeX - snowmanX) < SnowmanWidth / 2;
        if (snowmanAge > 32 || plowReached)
        {
            snowmanX = double.NaN; snowmanAge = 0; snowmanDelay = 40 + random.NextDouble() * 40;
        }
    }
    private bool SnowmanAreaVisible(double x, double y)
    {
        return IsDesktopVisible(x, y - 1) && IsDesktopVisible(x, y - SnowmanHeight + 4)
            && IsDesktopVisible(x - SnowmanWidth / 2 + 4, y - SnowmanHeight / 2)
            && IsDesktopVisible(x + SnowmanWidth / 2 - 4, y - SnowmanHeight / 2);
    }
    private void DrawSnowman(DrawingContext dc)
    {
        if (!settings.Snowman || !settings.Snowdrifts || double.IsNaN(snowmanX) || !SnowmanAreaVisible(snowmanX, snowmanGround)) return;
        double opacity = Math.Clamp(Math.Min(snowmanAge, 32 - snowmanAge), 0, 1);
        if (opacity <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawImage(SnowmanImage, new Rect(snowmanX - SnowmanWidth / 2, snowmanGround - SnowmanHeight, SnowmanWidth, SnowmanHeight));
        dc.Pop();
    }
    private void DrawSnowplow(DrawingContext dc)
    {
        if (!settings.Snowplow || !settings.Snowdrifts || double.IsNaN(plowX) || plowWait > 0) return;
        // The image contains transparent padding beneath its tracks.
        dc.DrawImage(PlowImage, new Rect(plowX, groundY - PlowHeight * 0.94, PlowWidth, PlowHeight));
    }
    private void UpdateGround()
    {
        var b = screen.Bounds;
        var work = screen.WorkingArea;
        double sx = ActualWidth / b.Width, sy = ActualHeight / b.Height;
        double newLeft = (work.Left - b.Left) * sx;
        double newRight = (work.Right - b.Left) * sx;
        double newGround = (work.Bottom - b.Top) * sy;
        if (workLeft != newLeft || workRight != newRight || groundY != newGround) driftDirty = true;
        workLeft = newLeft; workRight = newRight; groundY = newGround;
        int count = Math.Clamp((int)((workRight - workLeft) / 12), 24, 350);
        if (drifts.Length != count) { drifts = new double[count]; driftDirty = true; }
    }
    private double VisualHeight(int i)
    {
        double x = (workRight - workLeft) * i / (drifts.Length - 1);
        return DriftShape.Height(drifts[i], settings.DriftHeight, x);
    }
    private double CurrentHeight(double x)
    {
        if (!settings.Snowdrifts || drifts.Length < 2 || workRight <= workLeft) return 0;
        double position = Math.Clamp((x - workLeft) / (workRight - workLeft) * (drifts.Length - 1), 0, drifts.Length - 1);
        int left = (int)position, right = Math.Min(left + 1, drifts.Length - 1);
        double fraction = position - left;
        return VisualHeight(left) * (1 - fraction) + VisualHeight(right) * fraction;
    }
    private void AddSnow(double x)
    {
        if (drifts.Length == 0 || workRight <= workLeft) return;
        int center = Math.Clamp((int)((x - workLeft) / (workRight - workLeft) * (drifts.Length - 1)), 0, drifts.Length - 1);
        driftDirty = true;
        for (int d = -4; d <= 4; d++)
        {
            int i = center + d;
            if (i < 0 || i >= drifts.Length) continue;
            double addition = 2.4 * Math.Exp(-d * d / 6.0);
            drifts[i] = Math.Min(settings.DriftHeight, drifts[i] + addition);
        }
    }
    private void UpdateFlakeVisibility(Flake f, double dt)
    {
        // Gradually reveal flakes uncovered by a moving window, rather than
        // instantly painting every previously hidden particle at full opacity.
        bool visible = IsDesktopVisible(f.X, f.Y);
        f.Visibility = visible ? Math.Min(1, f.Visibility + dt / 0.45) : 0;
    }
    private bool IsDesktopVisible(double x, double y)
    {
        var point = new Point(x, y);
        if (!workRect.Contains(point)) return false;
        foreach (var occupied in occupiedRects) if (occupied.Contains(point)) return false;
        return true;
    }
    private void SweepSnow(Point from, Point to)
    {
        if (drifts.Length == 0 || workRight <= workLeft || (to - from).Length < 2) return;
        double upper = groundY - settings.DriftHeight - 25;
        if (from.Y < upper || from.Y > groundY + 4 || to.Y < upper || to.Y > groundY + 4 || !IsDesktopVisible(to.X, Math.Min(to.Y, groundY - 1))) return;
        double left = Math.Max(workLeft, Math.Min(from.X, to.X) - 20);
        double right = Math.Min(workRight, Math.Max(from.X, to.X) + 20);
        if (left > right) return;
        int first = Math.Clamp((int)((left - workLeft) / (workRight - workLeft) * (drifts.Length - 1)), 0, drifts.Length - 1);
        int last = Math.Clamp((int)((right - workLeft) / (workRight - workLeft) * (drifts.Length - 1)), 0, drifts.Length - 1);
        for (int i = first; i <= last; i++) drifts[i] = Math.Max(0, drifts[i] - 5);
        driftDirty = true;
    }
    private void EnsureBrushes()
    {
        if (previousOpacity != settings.Opacity)
        {
            previousOpacity = settings.Opacity;
            for (int i = 0; i < snowBrushes.Length; i++)
            {
                var c = Color.FromArgb((byte)(settings.Opacity * 255 * i / (100 * 16)), 245, 251, 255);
                var brush = new SolidColorBrush(c); brush.Freeze(); snowBrushes[i] = brush;
            }
        }
        if (previousDriftOpacity != settings.DriftOpacity)
        {
            previousDriftOpacity = settings.DriftOpacity;
            byte alpha = (byte)(settings.DriftOpacity * 255 / 100);
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 243, 249, 255), 0.42));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 204, 223, 249), 1));
            gradient.Freeze(); driftBrush = gradient;
        }
    }
    private void DrawSnowdrifts(DrawingContext dc)
    {
        if (!settings.Snowdrifts || drifts.Length == 0 || workRight <= workLeft || driftBrush is null) return;
        if (driftDirty || previousDriftLimit != settings.DriftHeight)
        {
            previousDriftLimit = settings.DriftHeight; driftDirty = false;
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(new Point(workLeft, groundY), true, true);
                double dx = (workRight - workLeft) / (drifts.Length - 1);
                context.LineTo(new Point(workLeft, groundY - VisualHeight(0)), true, false);
                for (int i = 1; i < drifts.Length; i++)
                {
                    double previous = VisualHeight(i - 1);
                    double current = VisualHeight(i);
                    var control = new Point(workLeft + dx * (i - 1), groundY - previous);
                    var midpoint = new Point(workLeft + dx * (i - 0.5), groundY - (previous + current) / 2);
                    context.QuadraticBezierTo(control, midpoint, true, false);
                }
                context.LineTo(new Point(workRight, groundY - VisualHeight(drifts.Length - 1)), true, false);
                context.LineTo(new Point(workRight, groundY), true, false);
            }
            path.Freeze(); driftGeometry = path;
        }
        dc.DrawGeometry(driftBrush, null, driftGeometry);
    }
    private void UpdateDesktopClip()
    {
        var b = screen.Bounds;
        var work = screen.WorkingArea;
        double sx = ActualWidth / b.Width, sy = ActualHeight / b.Height;
        workRect = new Rect((work.Left - b.Left) * sx, (work.Top - b.Top) * sy, work.Width * sx, work.Height * sy);
        occupiedRects.Clear();
        Geometry clip = new RectangleGeometry(workRect);
        foreach (var w in windows)
        {
            var r = w.Bounds;
            int left = Math.Max(r.Left, work.Left), top = Math.Max(r.Top, work.Top);
            int right = Math.Min(r.Right, work.Right), bottom = Math.Min(r.Bottom, work.Bottom);
            if (left >= right || top >= bottom) continue;
            var occupied = new Rect((left - b.Left) * sx, (top - b.Top) * sy, (right - left) * sx, (bottom - top) * sy);
            occupiedRects.Add(occupied);
            var block = new RectangleGeometry(occupied);
            clip = Geometry.Combine(clip, block, GeometryCombineMode.Exclude, null);
            if (clip.IsEmpty()) break;
        }
        if (clip.CanFreeze) clip.Freeze();
        desktopClip = clip;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!Active || ActualWidth <= 0 || ActualHeight <= 0 || desktopClip.IsEmpty()) return;
        dc.PushClip(desktopClip);
        EnsureBrushes();
        DrawSnowdrifts(dc);
        DrawSnowman(dc);
        DrawSpray(dc);
        DrawSnowplow(dc);
        if (settings.FallingSnow)
            foreach (var f in flakes)
            {
                double opacity = f.Visibility * (f.LandedWindow != IntPtr.Zero ? Math.Max(0, 1 - f.MeltTime / 0.8) : 1);
                int index = Math.Clamp((int)Math.Round(opacity * 16), 0, 16);
                if (index == 0) continue;
                double radius = f.Size * settings.FlakeSize / 100.0;
                dc.DrawEllipse(snowBrushes[index], null, new Point(f.X, f.Y), radius, radius);
            }
        dc.Pop();
    }
}
