namespace SnowWindows;

// One gentle stop/back-up/retry, measured in seconds, independent of frame rate.
internal sealed class PlowStuckAnimation
{
    internal const double CooldownSeconds = 180;
    internal const double Retreat = 18;
    private double elapsed;
    internal double Origin { get; private set; }
    internal bool Active { get; private set; }
    internal static bool CanStart(bool enabled, bool selectedPass, bool alreadyTriggered, double secondsSinceLast, double height, double maxHeight)
        => enabled && selectedPass && !alreadyTriggered && secondsSinceLast >= CooldownSeconds && maxHeight > 0 && height >= maxHeight * .60;
    internal static bool HasRetreatRoom(double blade, double left, Func<double, bool> isVisible)
        => blade - Retreat >= left && isVisible(blade) && isVisible(blade - Retreat);
    internal void Start(double origin) { Origin = origin; elapsed = 0; Active = true; }
    internal void Cancel() { Active = false; elapsed = 0; }
    private static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static double Offset(double t)
    {
        if (t < .8) return 0;
        if (t < 1.3) return -Retreat * Ease((t - .8) / .5);
        if (t < 1.5) return -Retreat;
        if (t < 2.1) return -Retreat * (1 - Ease((t - 1.5) / .6));
        return 0;
    }
    // Pause the entire sequence if an application window covers its path.
    internal double Step(double dt, Func<double, bool> positionVisible)
    {
        if (!Active) return Origin;
        double next = Math.Min(2.1, elapsed + Math.Max(0, dt));
        double x = Origin + Offset(next);
        if (!positionVisible(x)) return Origin + Offset(elapsed);
        elapsed = next;
        if (elapsed >= 2.1) Active = false;
        return x;
    }
}
