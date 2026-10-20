using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Byte.Core;
using Forms = System.Windows.Forms;

namespace Byte.App;

internal sealed class CompanionHost : IDisposable
{
    private readonly Storage storage = new();
    private readonly Settings settings;
    private readonly Companion bot;
    private List<Display> displays;
    private readonly Overlay overlay;
    private readonly Bubble bubble = new();
    private readonly Hotkeys hotkeys;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double previous, lastEnvironmentCheck, bubbleUntil, busyUntil, interactionUntil;
    private PointD previousPointer;
    private bool hidden, fullscreen, suspended, locked, disposed, lastMouseDown;
    private SettingsWindow? preferences;
    private readonly bool smoke;
    private readonly IntPtr initialForeground;

    public CompanionHost(string[] args)
    {
        smoke = args.Contains("--smoke-test");
        initialForeground = Native.GetForegroundWindow();
        settings = storage.Load();
        displays = Native.Displays();
        bot = new(displays, settings);
        RefreshMotion();
        overlay = new(bot);
        overlay.Show();
        overlay.RenderPosition();
        hotkeys = new(overlay.Handle);
        string? warning = storage.Warning;
        try { hotkeys.Set(settings.SummonHotkey, settings.HideHotkey); }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception)
        {
            Storage.Log(ex);
            warning = "Byte's hotkeys are unavailable. Use tray Preferences to choose new shortcuts.";
        }
        if (settings.LaunchAtLogin && !smoke)
        {
            try { Storage.SetStartup(true); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                Storage.Log(ex);
                warning = "Windows could not enable launch at login. Please check Preferences.";
            }
        }
        if (!Native.WTSRegisterSessionNotification(overlay.Handle, 0))
            Storage.Log(new System.ComponentModel.Win32Exception("Windows session notifications could not be registered."));
        icon = TrayIcon.Create();
        tray = new Forms.NotifyIcon { Text = "Byte · your tiny terminal friend", Icon = icon, Visible = true };
        tray.DoubleClick += (_, _) => Dispatch(Summon);
        BuildTray();
        overlay.Clicked += ToggleMenu;
        overlay.DoubleClicked += () => Interact(Mood.Celebrate);
        overlay.DragBegan += () => bubble.Hide();
        bot.Speech += text =>
        {
            bubble.Speech(text);
            bubble.Present(bot);
            bubbleUntil = clock.Elapsed.TotalSeconds + 8;
            interactionUntil = bubbleUntil;
        };
        overlay.NativeEvent += OnNativeEvent;
        displayTimer.Tick += (_, _) => { displayTimer.Stop(); RecoverDisplays(); };
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += Tick;
        previousPointer = Native.Cursor();
        timer.Start();
        if (smoke)
        {
            var output = args.SkipWhile(a => a != "--smoke-test").Skip(1).FirstOrDefault();
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("--smoke-test requires a report path.");
            var testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            testTimer.Tick += async (_, _) =>
            {
                testTimer.Stop();
                try
                {
                    bot.Paused = true;
                    await SmokeTests.Run(overlay, bubble, bot, displays, initialForeground, output);
                    Application.Current.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Storage.Log(ex);
                    System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() },
                        new JsonSerializerOptions { WriteIndented = true }));
                    Application.Current.Shutdown(2);
                }
            };
            testTimer.Start();
        }
        else if (warning is not null)
            tray.ShowBalloonTip(7000, "Byte · settings need attention", warning, Forms.ToolTipIcon.Warning);
    }

    private static void Dispatch(Action action) => Application.Current.Dispatcher.Invoke(action);
    private void RefreshMotion() => bot.ReducedMotion = settings.ReducedMotion ||
        (settings.RespectSystemReducedMotion && Native.SystemReducedMotion());
    private void OnNativeEvent(int msg, IntPtr wparam)
    {
        if (msg == Native.WmHotkey)
        {
            if (wparam.ToInt32() == 1) Summon();
            else if (wparam.ToInt32() == 2) ToggleHide();
        }
        else if (msg is 0x007E or 0x001A) displayTimer.Start();
        else if (msg == 0x02E0)
            Application.Current.Dispatcher.BeginInvoke(() => { overlay.InvalidatePlacement(); overlay.RenderPosition(); });
        else if (msg == 0x0218)
        {
            if (wparam.ToInt32() == 4) { suspended = true; bubble.Hide(); overlay.Hide(); }
            else if (wparam.ToInt32() is 7 or 18)
            {
                suspended = false;
                previous = clock.Elapsed.TotalSeconds;
                displayTimer.Start();
            }
        }
        else if (msg == 0x02B1)
        {
            if (wparam.ToInt32() == 7) { locked = true; bubble.Hide(); overlay.Hide(); }
            else if (wparam.ToInt32() == 8) { locked = false; displayTimer.Start(); }
        }
    }

    private void RecoverDisplays()
    {
        displays = Native.Displays();
        overlay.CancelInteraction();
        bot.UpdateDisplays(displays);
        RefreshMotion();
        bubble.Hide();
        overlay.InvalidatePlacement();
        if (!hidden && !fullscreen && !locked && !suspended) { overlay.Show(); overlay.RenderPosition(); }
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        var elapsed = now - previous;
        previous = now;
        var pointer = Native.Cursor();
        var mouseDown = Native.LeftMouseDown;
        if (bubble.IsVisible && mouseDown && !lastMouseDown && !bubble.ContainsScreen(pointer))
        {
            var local = overlay.Character.PointFromScreen(new Point(pointer.X, pointer.Y));
            if (!overlay.Character.Interactive(local)) bubble.Hide();
        }
        lastMouseDown = mouseDown;
        if ((pointer - previousPointer).Length > 4) busyUntil = now + 2;
        previousPointer = pointer;
        if (now - lastEnvironmentCheck >= .75)
        {
            lastEnvironmentCheck = now;
            fullscreen = !smoke && settings.PauseForFullscreen && Native.Fullscreen(displays);
            if (hidden || fullscreen || suspended || locked)
            {
                if (overlay.IsVisible) { overlay.CancelInteraction(); overlay.Hide(); }
                bubble.Hide();
            }
            else if (!overlay.IsVisible) { overlay.Show(); overlay.InvalidatePlacement(); }
        }
        var busy = now < busyUntil && now >= interactionUntil;
        bot.PointerMoved(busy);
        if (!hidden && !fullscreen && !suspended && !locked)
        {
            var paused = bot.Paused;
            bot.Paused = paused || bubble.IsVisible || preferences is not null;
            bot.Tick(elapsed, pointer, busy);
            bot.Paused = paused;
            overlay.RenderPosition();
        }
        else if (bot.IsFocusing)
        {
            var paused = bot.Paused;
            bot.Paused = true;
            bot.Tick(elapsed, pointer, true);
            bot.Paused = paused;
        }
        if (bubble.IsVisible && now >= bubbleUntil) bubble.Hide();
        var animated = bot.Mood is not (Mood.Idle or Mood.Sleep or Mood.Sit or Mood.Focus);
        timer.Interval = TimeSpan.FromMilliseconds(hidden || fullscreen || suspended || locked ? 250 :
            bubble.IsVisible ? 16 : bot.ReducedMotion || bot.Paused ? 200 : animated ? 16 : 100);
    }

    private void ToggleMenu()
    {
        if (bubble.IsVisible) { bubble.Hide(); return; }
        bubble.Menu(bot, Interact, bot.TellJoke, () =>
        {
            if (bot.IsFocusing) bot.StopFocus(); else bot.StartFocus(settings.FocusMinutes);
            bubble.Hide();
            BuildTray();
        }, ToggleHide, OpenSettings, NextMonitor);
        bubble.Present(bot);
        bubbleUntil = clock.Elapsed.TotalSeconds + 18;
    }

    private void Interact(Mood mood)
    {
        bubble.Hide();
        bot.Act(mood);
        interactionUntil = clock.Elapsed.TotalSeconds + 6;
        if (!settings.Muted && mood == Mood.Celebrate) System.Media.SystemSounds.Asterisk.Play();
    }
    private void NextMonitor()
    {
        bubble.Hide();
        if (displays.Count == 1)
        {
            bubble.Speech("One screen, one tiny kingdom.");
            bubble.Present(bot);
            bubbleUntil = clock.Elapsed.TotalSeconds + 4;
            return;
        }
        var index = displays.FindIndex(d => d.Id == bot.CurrentDisplay.Id);
        bot.JumpTo(displays[(index + 1) % displays.Count]);
        interactionUntil = clock.Elapsed.TotalSeconds + 4;
    }
    private void ToggleHide()
    {
        hidden = !hidden;
        bubble.Hide();
        overlay.CancelInteraction();
        if (hidden) overlay.Hide();
        else if (!fullscreen && !locked && !suspended) { overlay.Show(); overlay.RenderPosition(); }
        BuildTray();
    }
    private void Summon()
    {
        hidden = false;
        bubble.Hide();
        overlay.CancelInteraction();
        bot.Summon(Native.Cursor());
        if (!fullscreen && !locked && !suspended) { overlay.Show(); overlay.RenderPosition(); }
        BuildTray();
    }
    private void OpenSettings()
    {
        bubble.Hide();
        if (preferences is not null) { preferences.Activate(); return; }
        preferences = new(settings, ApplySettings);
        preferences.Closed += (_, _) => preferences = null;
        preferences.Show();
    }
    private void ApplySettings(Settings next)
    {
        next.Validate();
        hotkeys.Set(next.SummonHotkey, next.HideHotkey);
        try
        {
            Storage.SetStartup(next.LaunchAtLogin);
            storage.Save(next);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            hotkeys.Set(settings.SummonHotkey, settings.HideHotkey);
            Storage.SetStartup(settings.LaunchAtLogin);
            throw;
        }
        foreach (var property in typeof(Settings).GetProperties()) property.SetValue(settings, property.GetValue(next));
        RefreshMotion();
        bot.UpdateDisplays(displays);
        overlay.InvalidatePlacement();
        overlay.RenderPosition();
        BuildTray();
    }
    private void Persist(Action change)
    {
        change();
        try { storage.Save(settings); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Storage.Log(ex);
            tray.ShowBalloonTip(5000, "Byte couldn't save preferences", ex.Message, Forms.ToolTipIcon.Error);
        }
        BuildTray();
    }
    private void BuildTray()
    {
        var old = tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        Add("Summon Byte", Summon);
        Add(hidden ? "Show Byte" : "Hide Byte", ToggleHide);
        Add(bot.Paused ? "Resume roaming" : "Pause roaming", () => { bot.Paused = !bot.Paused; bubble.Hide(); BuildTray(); }, bot.Paused);
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var level in Enum.GetValues<ActivityLevel>())
            Add(level.ToString(), () => Persist(() => settings.Activity = level), settings.Activity == level);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(settings.Muted ? "Unmute interaction sounds" : "Mute", () => Persist(() => settings.Muted = !settings.Muted), settings.Muted);
        Add(bot.IsFocusing ? "End focus timer" : "Focus with me", () =>
        {
            if (bot.IsFocusing) bot.StopFocus(); else bot.StartFocus(settings.FocusMinutes);
            BuildTray();
        });
        Add("Preferences…", OpenSettings);
        Add("Quit Byte", () => Application.Current.Shutdown());
        tray.ContextMenuStrip = menu;
        old?.Dispose();
        void Add(string text, Action action, bool check = false)
        {
            var item = new Forms.ToolStripMenuItem(text) { Checked = check };
            item.Click += (_, _) => Dispatch(action);
            menu.Items.Add(item);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop(); displayTimer.Stop(); hotkeys.Dispose();
        Native.WTSUnRegisterSessionNotification(overlay.Handle);
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose(); icon.Dispose();
        preferences?.Close(); bubble.Close(); overlay.Close();
    }
}
