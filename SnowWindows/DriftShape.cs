namespace SnowWindows;

// Stable, small hills based on already accumulated snow. Cleared cells stay empty.
internal static class DriftShape
{
    internal static double Height(double rawHeight, int maximum, double x)
    {
        double snow = Math.Clamp(rawHeight, 0, maximum);
        double hills = 0.86 + 0.09 * Math.Sin(x / 65.0) + 0.05 * Math.Sin(x / 27.0 + 1.1);
        return snow * hills;
    }
}
