using System.Runtime.InteropServices;

namespace SnippingScreen;

internal static class NativeMethods
{
    public const int HotKeyId = 0x534E4950;
    public const int ModControl = 0x0002;
    public const int ModWin = 0x0008;
    public const int VkSnapshot = 0x2C;
    public const int WmHotKey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static Rectangle? FindSmallestVisibleWindowAt(Point screenPoint, IntPtr excludedWindow)
    {
        Rectangle? result = null;
        var bestArea = long.MaxValue;

        EnumWindows((hWnd, _) =>
        {
            if (hWnd == excludedWindow || !IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out var rect))
            {
                return true;
            }

            var bounds = rect.ToRectangle();
            if (bounds.Width < 32 || bounds.Height < 32 || !bounds.Contains(screenPoint))
            {
                return true;
            }

            var area = (long)bounds.Width * bounds.Height;
            if (area < bestArea)
            {
                bestArea = area;
                result = bounds;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect
    {
        private readonly int Left;
        private readonly int Top;
        private readonly int Right;
        private readonly int Bottom;

        public Rectangle ToRectangle()
        {
            return Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }
    }
}
