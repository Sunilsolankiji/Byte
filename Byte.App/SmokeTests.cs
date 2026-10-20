using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Byte.Core;

namespace Byte.App;

internal static class SmokeTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

    public static void RunProbe(string[] args)
    {
        var path = args[1];
        var app = new Application();
        var window = new Window
        {
            Title = "Byte click-through test target (separate process)", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false,
            Topmost = true, Background = new SolidColorBrush(Color.FromRgb(26, 43, 50)),
            Content = new TextBlock { Text = "Byte native hit-test target\nNo desktop capture is performed.", Foreground = Brushes.White, Margin = new Thickness(8) },
            Left = double.Parse(args[2], CultureInfo.InvariantCulture),
            Top = double.Parse(args[3], CultureInfo.InvariantCulture),
            Width = 340, Height = 430
        };
        window.SourceInitialized += (_, _) =>
        {
            Native.Configure(window);
            var hwnd = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == Native.WmMouseActivate) { handled = true; return new IntPtr(3); }
                if (msg == 0x0202) File.AppendAllText(path + ".clicks", "click\n");
                return IntPtr.Zero;
            });
            File.WriteAllText(path, hwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
        };
        app.Run(window);
    }

    internal static async Task Click(Point point)
    {
        if (!SetCursorPos((int)point.X, (int)point.Y)) throw new InvalidOperationException("Could not move test pointer.");
        await Task.Delay(80);
        SetCursorPos((int)point.X, (int)point.Y);
        var down = new[] { new Input { Mouse = new MouseInput { Flags = 0x0002 } } };
        if (SendInput(1, down, Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Windows blocked test mouse down.");
        await Task.Delay(80);
        var up = new[] { new Input { Mouse = new MouseInput { Flags = 0x0004 } } };
        if (SendInput(1, up, Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Windows blocked test mouse up.");
        await Task.Delay(150);
    }

    internal static void RestorePointer(PointD pointer)
    {
        SendInput(1, new[] { new Input { Mouse = new MouseInput { Flags = 0x0004 } } }, Marshal.SizeOf<Input>());
        SetCursorPos((int)pointer.X, (int)pointer.Y);
    }

    public static async Task Run(Overlay overlay, Bubble bubble, Companion bot, IReadOnlyList<Display> displays, IntPtr foreground, string output)
    {
        var checks = new List<string>();
        var inputEvents = new List<string>();
        overlay.InputDiagnostic = inputEvents.Add;
        void Check(bool valid, string label)
        {
            if (!valid) throw new InvalidOperationException("Native smoke check failed: " + label);
            checks.Add(label);
        }
        bool ByteHasFocus()
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            return pid == Environment.ProcessId;
        }
        var originalPointer = Native.Cursor();
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, "probe-" + Guid.NewGuid().ToString("N") + ".tmp");
        Process? process = null;
        IntPtr target = IntPtr.Zero;
        try
        {
            Check(!ByteHasFocus(), "Overlay startup does not take keyboard focus");
            var style = Native.GetWindowLongPtr(overlay.Handle, Native.GwlExStyle).ToInt64();
            Check((style & Native.NoActivate) != 0 && (style & Native.ToolWindow) != 0,
                "Native NOACTIVATE and TOOLWINDOW styles are set");
            Check((style & 0x80000) != 0, "Overlay is a real layered HWND");
            var main = displays.FirstOrDefault(d => d.Primary) ?? displays[0];
            var center = main.Work.Center;
            bot.Summon(new(center.X, center.Y));
            bot.StartDrag();
            bot.Drag(new PointD(center.X, Math.Min(main.Work.Bottom - 40, center.Y + 180)));
            bot.Release(default);
            bot.Paused = true;
            overlay.RenderPosition();
            await Task.Delay(200);
            var probeLeft = bot.Feet.X - 170;
            var probeTop = bot.Feet.Y - 370;
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add("--probe");
            start.ArgumentList.Add(probePath);
            start.ArgumentList.Add((probeLeft / main.Scale).ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add((probeTop / main.Scale).ToString(CultureInfo.InvariantCulture));
            process = Process.Start(start) ?? throw new InvalidOperationException("Probe process failed to start.");
            for (var i = 0; i < 100 && !File.Exists(probePath); i++) await Task.Delay(50);
            Check(File.Exists(probePath), "Separate-process underlying test window starts");
            target = new IntPtr(long.Parse(File.ReadAllText(probePath), CultureInfo.InvariantCulture));
            // Correct any initial WPF scaling: all subsequent placement is in physical coordinates.
            Native.Place(target, (int)probeLeft, (int)probeTop, 340, 430);
            overlay.InvalidatePlacement();
            overlay.RenderPosition();
            await Task.Delay(250);
            var transparentPoint = overlay.Character.PointToScreen(new Point(4, 4));
            var bodyPoint = overlay.Character.PointToScreen(new Point(77, 88));
            Check(Native.WindowFromPoint(new Native.Point { X = (int)transparentPoint.X, Y = (int)transparentPoint.Y }) == target,
                "Transparent pixels hit the underlying HWND in a separate process");
            Check(Native.WindowFromPoint(new Native.Point { X = (int)bodyPoint.X, Y = (int)bodyPoint.Y }) == overlay.Handle,
                "Opaque character pixels hit Byte");
            await Click(transparentPoint);
            Check(File.Exists(probePath + ".clicks") && File.ReadAllLines(probePath + ".clicks").Length == 1,
                "Actual injected click passes through to a different process");
            Check(!ByteHasFocus() && Native.GetForegroundWindow() != target,
                "Pass-through click does not activate Byte or its test target" +
                ((!ByteHasFocus() && Native.GetForegroundWindow() != target) ? "" :
                    "; foreground=" + Native.GetForegroundWindow() + "; initial=" + foreground +
                    "; overlay=" + overlay.Handle + "; target=" + target + "; input=" + string.Join("; ", inputEvents)));
            await Click(bodyPoint);
            await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 150);
            Check(bubble.IsVisible, "Real character click opens the interaction bubble" +
                (bubble.IsVisible ? "" : "; input: " + string.Join("; ", inputEvents) +
                "; pointer: " + Native.Cursor() + "; intended: " + bodyPoint));
            Check(!ByteHasFocus(), "Opening interaction bubble preserves keyboard focus");
            var bubbleHandle = new WindowInteropHelper(bubble).Handle;
            Native.GetWindowRect(bubbleHandle, out var bubbleRect);
            Check(bubbleRect.Left >= main.Work.Left && bubbleRect.Top >= main.Work.Top &&
                bubbleRect.Right <= main.Work.Right && bubbleRect.Bottom <= main.Work.Bottom, "Bubble remains in monitor work area");
            var bubblePoint = new Native.Point { X = bubbleRect.Left + 20, Y = bubbleRect.Top + 25 };
            Check(Native.WindowFromPoint(bubblePoint) == bubbleHandle, "Open bubble receives its own hit testing");
            bubble.Hide();
            Check(Native.WindowFromPoint(bubblePoint) != bubbleHandle, "Closed bubble no longer intercepts the desktop");
            await Task.Delay(400);
            await Click(bodyPoint);
            await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime + 100);
            Check(bubble.IsVisible, "Bubble can reopen after dismissal");
            // Outside clicks are passed through, not consumed. Host polling is exercised separately below.
            var clicksBefore = File.ReadAllLines(probePath + ".clicks").Length;
            await Click(transparentPoint);
            Check(File.ReadAllLines(probePath + ".clicks").Length == clicksBefore + 1,
                "Outside-bubble click still reaches a separate-process window");
            Check(!bubble.IsVisible, "Outside click closes bubble without swallowing the click");
            bubble.Hide();

            var packed = new IntPtr(((int)transparentPoint.Y & 0xffff) << 16 | ((int)transparentPoint.X & 0xffff));
            Check(SendMessage(overlay.Handle, Native.WmNcHitTest, IntPtr.Zero, packed).ToInt32() == -1,
                "Native hit-test rejects non-character region");
            Check(SendMessage(overlay.Handle, Native.WmMouseActivate, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3,
                "Mouse activation explicitly returns MA_NOACTIVATE");
            overlay.Hide();
            Check(Native.WindowFromPoint(new Native.Point { X = (int)bodyPoint.X, Y = (int)bodyPoint.Y }) == target,
                "Hidden Byte immediately releases its interactive area");
            overlay.Show(); overlay.InvalidatePlacement(); overlay.RenderPosition();
            Check(!ByteHasFocus(), "Hide/show does not steal focus");

            bot.Summon(main.Work.Center);
            overlay.RenderPosition();
            await Task.Delay(150);
            var dragPoint = overlay.Character.PointToScreen(new Point(77, 88));
            SetCursorPos((int)dragPoint.X, (int)dragPoint.Y);
            await Task.Delay(100);
            Check(SendInput(1, new[] { new Input { Mouse = new MouseInput { Flags = 0x0002 } } }, Marshal.SizeOf<Input>()) == 1,
                "Native drag mouse-down accepted");
            await Task.Delay(100);
            for (var i = 1; i <= 6; i++)
            {
                SetCursorPos((int)dragPoint.X + i * 10, (int)dragPoint.Y - i * 16);
                await Task.Delay(30);
            }
            Check(bot.Mood == Mood.Drag, "Real pointer movement captures and drags Byte. State: " + bot.Mood +
                "; input: " + string.Join("; ", inputEvents));
            Check(SendInput(1, new[] { new Input { Mouse = new MouseInput { Flags = 0x0004 } } }, Marshal.SizeOf<Input>()) == 1,
                "Native drag release accepted");
            await Task.Delay(100);
            Check(bot.Mood == Mood.Fall, "Real drag release enters toss physics");
            for (var i = 0; i < 100; i++)
            {
                bot.Paused = false; bot.Tick(.05, default, false); bot.Paused = true;
            }
            overlay.RenderPosition();
            Check(bot.Feet.Y == main.Work.Bottom, "Native drag and toss safely land on the work-area floor");
            await Task.Delay(650);
            overlay.RenderPosition();
            var lostUpPoint = overlay.Character.PointToScreen(new Point(77, 88));
            SetCursorPos((int)lostUpPoint.X, (int)lostUpPoint.Y);
            await Task.Delay(100);
            SendInput(1, new[] { new Input { Mouse = new MouseInput { Flags = 0x0002 } } }, Marshal.SizeOf<Input>());
            await Task.Delay(100);
            for (var i = 1; i <= 5; i++)
            {
                SetCursorPos((int)lostUpPoint.X + i * 8, (int)lostUpPoint.Y - i * 12);
                await Task.Delay(35);
            }
            Check(bot.Mood == Mood.Drag, "Lost-mouse-up regression begins with a real captured drag");
            overlay.SuppressNextMouseUp = true;
            SendInput(1, new[] { new Input { Mouse = new MouseInput { Flags = 0x0004 } } }, Marshal.SizeOf<Input>());
            await Task.Delay(450);
            Check(!overlay.SuppressNextMouseUp && bot.Mood == Mood.Fall && !overlay.IsMouseCaptured,
                "Release watchdog recovers when the native mouse-up message is lost");
            bot.CancelDrag();
            overlay.RenderPosition();
            bubble.Hide();
            await Task.Delay(600);
            var doublePoint = overlay.Character.PointToScreen(new Point(77, 88));
            await Click(doublePoint);
            await Click(doublePoint);
            await Task.Delay(100);
            Check(bot.Mood == Mood.Celebrate && !bubble.IsVisible, "Real double-click celebrates instead of opening a menu");

            if (displays.Count > 1)
            {
                var targetDisplay = displays.First(d => d.Id != main.Id);
                var goRight = targetDisplay.Work.Center.X >= main.Work.Center.X;
                bot.Summon(new PointD(goRight ? main.Work.Right : main.Work.Left, main.Work.Bottom));
                bot.Paused = false;
                bot.JumpTo(targetDisplay);
                bot.Paused = true;
                Check(bot.Mood == Mood.Windup, "Real-monitor transition starts with a windup");
                for (var i = 0; i < 46; i++)
                {
                    bot.Paused = false; bot.Tick(.05, default, false); bot.Paused = true;
                    overlay.RenderPosition();
                    await Task.Delay(16);
                }
                Check(bot.CurrentDisplay.Id == targetDisplay.Id && targetDisplay.Work.Contains(bot.Feet),
                    "Native overlay leaps and lands on the connected negative-coordinate monitor");
            }
            foreach (var display in displays)
            {
                bot.Summon(display.Work.Center);
                overlay.InvalidatePlacement(); overlay.RenderPosition();
                await Task.Delay(250);
                overlay.RenderPosition();
                Native.GetWindowRect(overlay.Handle, out var rect);
                Check(Math.Abs((rect.Right - rect.Left) - Desktop.SpriteWidth * display.Scale * bot.Settings.Size) <= 2 &&
                    rect.Left >= display.Work.Left && rect.Right <= display.Work.Right &&
                    rect.Top >= display.Work.Top && rect.Bottom <= display.Work.Bottom,
                    $"Native DPI placement and summon on {display.Id} ({display.Scale * 100:0}%)");
            }
            bot.UpdateDisplays(displays);
            Check(displays.Any(d => d.Work.Contains(bot.Feet)), "Display refresh keeps Byte on a real monitor");
            Check(Hotkeys.Parse("Ctrl+Alt+B") == (0x4003U, 0x42U), "Configurable hotkey parser");
            bubble.Hide();
            bot.Act(Mood.Idle);
            overlay.RenderPosition();
            Native.PostMessage(overlay.Handle, Native.WmHotkey, new IntPtr(2), IntPtr.Zero);
            await Task.Delay(150);
            Check(!overlay.IsVisible, "Quick-hide native hotkey message hides Byte");
            Native.PostMessage(overlay.Handle, Native.WmHotkey, new IntPtr(1), IntPtr.Zero);
            await Task.Delay(150);
            Check(overlay.IsVisible && displays.Any(d => d.Work.Contains(bot.Feet)), "Summon native hotkey message restores Byte safely");
            Check(!ByteHasFocus(), "Drag, double-click, monitor leap and hotkeys do not take focus");
            var art = new Character(bot);
            var visuals = new DrawingVisual();
            using (var context = visuals.RenderOpen())
            {
                var states = new[] { Mood.Idle, Mood.Celebrate, Mood.Sleep, Mood.Inspect, Mood.Sign, Mood.Cable, Mood.Bug, Mood.Warm, Mood.Blocks, Mood.Focus };
                for (var i = 0; i < states.Length; i++)
                {
                    bot.Act(states[i]);
                    art.InvalidateVisual();
                    art.Measure(new Size(154, 170)); art.Arrange(new Rect(0, 0, 154, 170)); art.UpdateLayout();
                    var frame = new RenderTargetBitmap(154, 170, 96, 96, PixelFormats.Pbgra32);
                    frame.Render(art);
                    context.PushTransform(new TranslateTransform(i % 5 * 174 + 10, i / 5 * 190 + 10));
                    context.DrawImage(frame, new Rect(0, 0, 154, 170));
                    context.Pop();
                }
            }
            var image = new RenderTargetBitmap(880, 390, 96, 96, PixelFormats.Pbgra32);
            image.Render(visuals);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(directory, "Byte-character.png"))) encoder.Save(file);
            Check(true, "Ten original character poses render without screen capture");
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                passed = true, time = DateTimeOffset.Now, actualMonitorCount = displays.Count,
                initialForegroundHandle = foreground.ToInt64(), finalForegroundHandle = Native.GetForegroundWindow().ToInt64(),
                monitors = displays, checks, screenCaptured = false,
                limitation = "Physical hot-plug, sleep/resume, and mixed-DPI hardware transitions require manual checks. Synthetic layouts are covered by Byte.Tests."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            RestorePointer(originalPointer);
            if (target != IntPtr.Zero) Native.PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (process is not null)
            {
                if (!process.HasExited && !process.WaitForExit(2000)) process.Kill();
                process.Dispose();
            }
            File.Delete(probePath);
            File.Delete(probePath + ".clicks");
        }
    }
}
