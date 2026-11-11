namespace SnowWindows;

internal static class WorldClock
{
    internal static bool IsNight(DateTime local) => local.Hour >= 19 || local.Hour < 7;
}
