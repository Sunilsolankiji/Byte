using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Byte.Core;

namespace Byte.App;

internal static class Native
{
    internal const int GwlExStyle = -20;
    internal const long NoActivate = 0x08000000, ToolWindow = 0x00000080;
    internal const int WmNcHitTest = 0x0084, WmMouseActivate = 0x0021, WmHotkey = 0x0312;
    private static readonly IntPtr Topmost = new(-1);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    public static bool LeftMouseDown => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    public static List<Display> Displays()
    {
        var result = new List<Display>();
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
            var hr = GetDpiForMonitor(monitor, 0, out var dpi, out _);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            result.Add(new(info.Device, ToRect(info.Monitor), ToRect(info.Work), dpi / 96.0, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero)) throw new Win32Exception();
        if (result.Count == 0) throw new InvalidOperationException("Windows did not report any active displays.");
        return result;
    }

    private static RectD ToRect(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
    public static PointD Cursor()
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception();
        return new(point.X, point.Y);
    }
    public static bool SystemReducedMotion() => SystemParametersInfo(0x1042, 0, out var enabled, 0) && !enabled;
    public static void Configure(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | NoActivate | ToolWindow));
    }

    public static void Place(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (!SetWindowPos(hwnd, Topmost, x, y, width, height, 0x0010 | 0x0040))
            throw new Win32Exception();
    }

    public static bool Fullscreen(IReadOnlyList<Display> displays)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow()) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        var name = new System.Text.StringBuilder(256);
        GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(hwnd, out var rect)) return false;
        foreach (var d in displays)
            if (rect.Left <= d.Bounds.Left && rect.Top <= d.Bounds.Top &&
                rect.Right >= d.Bounds.Right && rect.Bottom >= d.Bounds.Bottom) return true;
        return false;
    }

    public static PointD ScreenPoint(IntPtr lparam)
    {
        var value = lparam.ToInt64();
        return new((short)(value & 0xffff), (short)((value >> 16) & 0xffff));
    }
}
