using System.Runtime.InteropServices;
using System.Text;

namespace Nimbus;

// Oyun, sunum ya da F11 ile tam ekrana alınmış tarayıcı açıkken pencere gösterilmez.
internal static class FullScreen
{
    private const int QunsNotPresent = 1;
    private const int QunsPresentationMode = 4;
    private const int MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;

    public static bool IsActive() =>
        (SHQueryUserNotificationState(out var state) == 0 && BlocksPrompt(state)) || ForegroundCoversMonitor();

    // 1: ekran kilitli, 2: tam ekran uygulama, 3: Direct3D tam ekran, 4: sunum modu.
    internal static bool BlocksPrompt(int notificationState) =>
        notificationState is >= QunsNotPresent and <= QunsPresentationMode;

    internal static bool Covers(Rect window, Rect monitor) =>
        window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    // Windows, kenarlıksız tam ekran pencereleri her zaman bildirmez; bu yüzden ayrıca bakılır.
    private static bool ForegroundCoversMonitor()
    {
        var window = GetForegroundWindow();
        if (window == 0 || !GetWindowRect(window, out var rect))
            return false;

        // Masaüstü de tüm ekranı kaplar.
        var className = new StringBuilder(32);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW")
            return false;

        // Görev çubuğu otomatik gizliyse normal büyütülmüş pencere de ekranı kaplar.
        if (IsZoomed(window) && (GetWindowLong(window, GwlStyle) & WsCaption) == WsCaption)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info) && Covers(rect, info.Monitor);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Rect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
