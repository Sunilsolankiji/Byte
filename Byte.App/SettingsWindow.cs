using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Byte.Core;

namespace Byte.App;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(Settings original, Action<Settings> save)
    {
        Title = "Byte · preferences";
        Width = 420; Height = 670;
        MinWidth = 360; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 248, 245));
        FontFamily = new FontFamily("Segoe UI");
        var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Small friend. Small footprint.", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Local-only. No accounts, AI, telemetry, screen capture,\ncode access, microphone, or keyboard monitoring.",
            Margin = new Thickness(0, 8, 0, 14), Foreground = Brushes.DarkSlateGray, FontSize = 11
        });
        Label("Personality");
        var activity = new ComboBox { ItemsSource = Enum.GetValues<ActivityLevel>(), SelectedItem = original.Activity };
        panel.Children.Add(activity);
        Label("Character size");
        var size = new Slider { Minimum = .65, Maximum = 1.6, Value = original.Size, TickFrequency = .05, IsSnapToTickEnabled = true };
        panel.Children.Add(size);
        var reduced = Check("Reduce motion (no roaming or animated flourishes)", original.ReducedMotion);
        var systemMotion = Check("Also respect Windows animation settings", original.RespectSystemReducedMotion);
        var pointer = Check("Occasional pointer play (never during focus)", original.PointerPlay);
        var fullscreen = Check("Automatically hide during fullscreen work", original.PauseForFullscreen);
        var startup = Check("Launch at login", original.LaunchAtLogin);
        var muted = Check("Mute interaction sounds", original.Muted);
        Label("Focus timer (1–180 minutes)");
        var focus = new TextBox { Text = original.FocusMinutes.ToString() }; panel.Children.Add(focus);
        Label("Summon to pointer's monitor");
        var summon = new TextBox { Text = original.SummonHotkey }; panel.Children.Add(summon);
        Label("Quick hide / show (presentations)");
        var hide = new TextBox { Text = original.HideHotkey }; panel.Children.Add(hide);
        panel.Children.Add(new TextBlock
        {
            Text = "Examples: Ctrl+Alt+B, Ctrl+Shift+F8. Hotkeys are\nregistered with Windows; Byte does not inspect typing.",
            FontSize = 10, Margin = new Thickness(0, 5, 0, 4), Foreground = Brushes.DarkSlateGray
        });
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var apply = new Button { Content = "Save", Padding = new Thickness(18, 6, 18, 6), IsDefault = true };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); panel.Children.Add(buttons);
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) =>
        {
            try
            {
                if (!int.TryParse(focus.Text, out var minutes) || minutes < 1 || minutes > 180)
                    throw new ArgumentException("Focus time must be between 1 and 180 minutes.");
                var next = new Settings
                {
                    Activity = (ActivityLevel)activity.SelectedItem, Size = size.Value,
                    ReducedMotion = reduced.IsChecked == true, RespectSystemReducedMotion = systemMotion.IsChecked == true,
                    PointerPlay = pointer.IsChecked == true, PauseForFullscreen = fullscreen.IsChecked == true,
                    LaunchAtLogin = startup.IsChecked == true, Muted = muted.IsChecked == true,
                    SummonHotkey = summon.Text.Trim(), HideHotkey = hide.Text.Trim(), FocusMinutes = minutes
                };
                save(next);
                Close();
            }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or
                System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Storage.Log(ex);
                error.Text = ex.Message;
            }
        };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 9, 0, 4), FontSize = 12 });
        CheckBox Check(string title, bool value)
        {
            var checkbox = new CheckBox { Content = title, IsChecked = value, Margin = new Thickness(0, 9, 0, 0), FontSize = 11 };
            panel.Children.Add(checkbox);
            return checkbox;
        }
    }
}
