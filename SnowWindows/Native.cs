using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SnowWindows;

internal static class Native
{
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    internal const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    internal static void MakeClickThrough(IntPtr hwnd)
    {
        long style = IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64() : GetWindowLong32(hwnd, GWL_EXSTYLE);
        style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, (int)style);
    }
    internal static RECT Bounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) == 0) return rect;
        GetWindowRect(hwnd, out rect); return rect;
    }
    internal static bool Cloaked(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int value, sizeof(int)) == 0 && value != 0;
    internal static bool IsOwnWindow(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }
    internal static bool IsDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return true;
        var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
    internal static bool IsFullscreen(IntPtr hwnd, System.Drawing.Rectangle screen)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || IsDesktop(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        var r = Bounds(hwnd);
        const int tolerance = 3;
        return r.Left <= screen.Left + tolerance && r.Top <= screen.Top + tolerance && r.Right >= screen.Right - tolerance && r.Bottom >= screen.Bottom - tolerance;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DisplayMode mode);
    internal static int DisplayRefreshRate(string deviceName)
    {
        var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>(), DeviceName = "", FormName = "" };
        if (!EnumDisplaySettings(deviceName, -1, ref mode)) return 0;
        // 0 and 1 mean the driver's default, not a usable frequency.
        return mode.DisplayFrequency is > 1 and <= 1000 ? (int)mode.DisplayFrequency : 0;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
    internal static bool AllowInterfaceAnimations()
    {
        // SPI_GETCLIENTAREAANIMATION; respect the Windows accessibility setting.
        return SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled != 0;
    }
    internal static bool ShouldPauseForForeground(IntPtr hwnd, System.Drawing.Rectangle screen)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || IsDesktop(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (IsFullscreen(hwnd, screen)) return true;
        if (!IsZoomed(hwnd)) return false;
        var r = Bounds(hwnd);
        return screen.Contains((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
    }
    internal readonly record struct WindowInfo(IntPtr Handle, RECT Bounds);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    internal static List<WindowInfo> VisibleWindowBounds()
    {
        var list = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsDesktop(hwnd) || Cloaked(hwnd)) return true;
            // Titled settings and dialogs should obscure snow too. Untitled
            // overlay surfaces are skipped to avoid hiding the whole desktop.
            if (GetWindowTextLength(hwnd) == 0) return true;
            var r = Bounds(hwnd);
            if (r.Right - r.Left >= 120 && r.Bottom - r.Top >= 80) list.Add(new WindowInfo(hwnd, r));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
