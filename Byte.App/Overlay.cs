using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Byte.Core;

namespace Byte.App;

internal sealed class Overlay : Window
{
    public Character Character { get; }
    public IntPtr Handle { get; private set; }
    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action? DragBegan;
    public event Action<int, IntPtr>? NativeEvent;
    internal Action<string>? InputDiagnostic { get; set; }
    private readonly Companion bot;
    private readonly Func<PointD?> readPointer;
    private PointD down, offset;
    private bool pressed, dragging;
    private long pressedAt;
    internal bool SuppressNextMouseUp { get; set; }
    private readonly Queue<(PointD Point, long Time)> samples = new();
    private readonly DispatcherTimer clickTimer = new();
    private (int X, int Y, int W, int H) lastBounds;

    public Overlay(Companion bot, Func<PointD?>? readPointer = null)
    {
        this.bot = bot;
        this.readPointer = readPointer ?? Native.ReadCursor;
        Title = "Byte desktop companion";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Width = 154;
        Height = 170;
        Left = -20000;
        Top = -20000;
        Character = new(bot);
        Content = new Viewbox { Child = Character, Stretch = Stretch.Fill };
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Native.Configure(this);
            HwndSource.FromHwnd(Handle).AddHook(Hook);
        };
        clickTimer.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime);
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); Clicked?.Invoke(); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) =>
        {
            InputDiagnostic?.Invoke("LostCapture pressed=" + pressed + " dragging=" + dragging);
            if (!pressed) return;
            pressed = false;
            if (dragging) bot.CancelDrag();
            dragging = false;
        };
        MouseRightButtonUp += (_, e) => { Clicked?.Invoke(); e.Handled = true; };
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == 0x001F) CancelInteraction();
        if (msg == 0x0202 && SuppressNextMouseUp)
        {
            SuppressNextMouseUp = false;
            handled = true;
            return IntPtr.Zero;
        }
        if (msg is 0x0201 or 0x0202 or 0x0021) InputDiagnostic?.Invoke("Native " + msg.ToString("X"));
        if (msg == Native.WmMouseActivate) { handled = true; return new IntPtr(3); }
        if (msg == Native.WmNcHitTest && !pressed)
        {
            var screen = Native.ScreenPoint(lparam);
            var p = Character.PointFromScreen(new Point(screen.X, screen.Y));
            handled = true;
            return new IntPtr(Character.Interactive(p) ? 1 : -1);
        }
        NativeEvent?.Invoke(msg, wparam);
        return IntPtr.Zero;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        InputDiagnostic?.Invoke("Down " + e.ClickCount + " at " + e.GetPosition(Character));
        if (e.ClickCount == 2)
        {
            clickTimer.Stop();
            DoubleClicked?.Invoke();
            e.Handled = true;
            return;
        }
        if (readPointer() is not PointD pointer) { CancelInteraction(); return; }
        down = pointer;
        offset = bot.Feet - down;
        pressed = true;
        pressedAt = Stopwatch.GetTimestamp();
        dragging = false;
        samples.Clear();
        samples.Enqueue((down, Stopwatch.GetTimestamp()));
        var captured = CaptureMouse();
        InputDiagnostic?.Invoke("Capture " + captured);
        if (!captured) pressed = false;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed) return;
        if (readPointer() is not PointD pointer) { CancelInteraction(); return; }
        if (!dragging && (pointer - down).Length > 5)
        {
            dragging = true;
            clickTimer.Stop();
            DragBegan?.Invoke();
            bot.StartDrag();
        }
        if (!dragging) return;
        bot.Drag(pointer + offset);
        var now = Stopwatch.GetTimestamp();
        samples.Enqueue((pointer, now));
        while (samples.Count > 2 && (now - samples.Peek().Time) / (double)Stopwatch.Frequency > .12) samples.Dequeue();
        RenderPosition();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        InputDiagnostic?.Invoke("Up pressed=" + pressed + " dragging=" + dragging);
        FinishRelease();
        e.Handled = true;
    }

    private void FinishRelease()
    {
        if (!pressed) return;
        if (readPointer() is not PointD pointer) { CancelInteraction(); return; }
        pressed = false;
        ReleaseMouseCapture();
        if (dragging)
        {
            var now = Stopwatch.GetTimestamp();
            var first = samples.Peek();
            var dt = Math.Max(.016, (now - first.Time) / (double)Stopwatch.Frequency);
            bot.Release((pointer - first.Point) * (1 / dt));
        }
        else clickTimer.Start();
        dragging = false;
    }

    public void CheckDragRelease()
    {
        if (pressed && (Stopwatch.GetTimestamp() - pressedAt) / (double)Stopwatch.Frequency > .15 &&
            !Native.LeftMouseDown) FinishRelease();
    }

    public void RenderPosition()
    {
        var scale = bot.CurrentDisplay.Scale * bot.Settings.Size;
        var width = (int)Math.Ceiling(Desktop.SpriteWidth * scale);
        var height = (int)Math.Ceiling(Desktop.SpriteHeight * scale);
        var x = (int)Math.Round(bot.Feet.X - width / 2.0);
        var y = (int)Math.Round(bot.Feet.Y - height);
        var bounds = (x, y, width, height);
        Native.GetWindowRect(Handle, out var actual);
        if (lastBounds != bounds || actual.Left != x || actual.Top != y ||
            actual.Right - actual.Left != width || actual.Bottom - actual.Top != height)
        {
            // Win32 owns physical placement; WPF owns per-monitor device-independent rendering.
            var dpi = Native.GetDpiForWindow(Handle) / 96.0;
            if (dpi <= 0) dpi = 1;
            Width = width / dpi;
            Height = height / dpi;
            Native.Place(Handle, x, y, width, height);
            lastBounds = bounds;
        }
        Character.InvalidateVisual();
    }

    public void InvalidatePlacement() => lastBounds = default;
    public void CancelInteraction()
    {
        clickTimer.Stop();
        pressed = false;
        dragging = false;
        ReleaseMouseCapture();
        bot.CancelDrag();
    }
}
