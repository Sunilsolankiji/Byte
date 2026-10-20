using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Byte.Core;

namespace Byte.App;

internal sealed class Bubble : Window
{
    private readonly Border border;
    public Bubble()
    {
        Title = "Byte interaction";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(19, 36, 44)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(105, 217, 185)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12)
        };
        Content = border;
        SourceInitialized += (_, _) =>
        {
            Native.Configure(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(
                (IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == Native.WmMouseActivate) { handled = true; return new IntPtr(3); }
                    return IntPtr.Zero;
                });
        };
    }

    public void Menu(Companion bot, Action<Mood> act, Action joke, Action focus, Action hide, Action settings, Action nextMonitor,
        Action? switchCharacter = null, Action? toggleFriends = null)
    {
        var panel = new StackPanel { MinWidth = 178 };
        panel.Children.Add(Label(bot.Kind.ToString().ToUpperInvariant() + " / tiny desktop friend", 10, "#8EC8B7"));
        panel.Children.Add(Label(bot.IsFocusing
            ? $"Focus • {Math.Ceiling(bot.FocusRemaining / 60)} min left" : "A small break in your stack.", 12, "#ECF8F3"));
        Add("Tell me a dev joke", joke);
        Add("Celebrate", () => act(Mood.Celebrate));
        Add(bot.IsFocusing ? "End focus timer" : $"Focus with me · {bot.Settings.FocusMinutes} min", focus);
        Add("Take a nap", () => act(Mood.Sleep));
        Add("Hop to next monitor", nextMonitor);
        if (switchCharacter is not null) Add("Switch character", switchCharacter);
        if (toggleFriends is not null) Add(bot.Settings.FriendsTogether ? "Just one character" : "Bring friends together", toggleFriends);
        Add("Settings", settings);
        Add("Hide Byte", hide);
        border.Child = panel;
        void Add(string title, Action action)
        {
            var button = new Button
            {
                Content = title, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 3, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(31, 57, 64)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0),
                FontSize = 12, Focusable = false, Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => action();
            panel.Children.Add(button);
        }
    }

    public void Speech(string text)
    {
        var panel = new StackPanel { MaxWidth = 260 };
        panel.Children.Add(Label(text, 13, "#EDFDF5"));
        var close = new Button
        {
            Content = "Got it", Foreground = Brushes.LightGray, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0)
        };
        close.Click += (_, _) => Hide();
        panel.Children.Add(close);
        border.Child = panel;
    }

    private static TextBlock Label(string value, double size, string color) => new()
    {
        Text = value, FontSize = size, FontFamily = new FontFamily("Segoe UI"),
        Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
        Margin = new Thickness(1, 2, 1, 6), TextWrapping = TextWrapping.Wrap
    };

    public void Present(Companion bot)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        var dpi = Native.GetDpiForWindow(new WindowInteropHelper(this).Handle) / 96.0;
        if (dpi <= 0) dpi = 1;
        var work = bot.CurrentDisplay.Work;
        var width = (int)Math.Ceiling(ActualWidth * dpi);
        var height = (int)Math.Ceiling(ActualHeight * dpi);
        var x = (int)Math.Clamp(bot.Feet.X - width / 2.0, work.Left, Math.Max(work.Left, work.Right - width));
        var desiredY = bot.Feet.Y - Desktop.SpriteHeight * bot.CurrentDisplay.Scale * bot.Settings.Size - height - 8;
        var y = (int)Math.Clamp(desiredY, work.Top, Math.Max(work.Top, work.Bottom - height));
        Native.Place(new WindowInteropHelper(this).Handle, x, y, width, height);
    }

    public bool ContainsScreen(PointD pointer)
    {
        if (!IsVisible) return false;
        var local = PointFromScreen(new Point(pointer.X, pointer.Y));
        return new Rect(0, 0, ActualWidth, ActualHeight).Contains(local);
    }
}
