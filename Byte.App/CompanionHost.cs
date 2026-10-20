using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
    private readonly List<(Companion Bot, Overlay Overlay)> residents = new();
    private readonly SocialBehavior social = new();
    private Companion menuBot;
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
        menuBot = bot;
        residents.Add((bot, overlay));
        WireResident(bot, overlay);
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
        overlay.NativeEvent += OnNativeEvent;
        if (!smoke) SyncFriends(true);
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
                var originalPointer = Native.Cursor();
                try
                {
                    bot.Paused = true;
                    await SmokeTests.Run(overlay, bubble, bot, displays, initialForeground, output);
                    await RunFriendsSmoke(output);
                    Application.Current.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Storage.Log(ex);
                    System.IO.File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() },
                        new JsonSerializerOptions { WriteIndented = true }));
                    Application.Current.Shutdown(2);
                }
                finally { SmokeTests.RestorePointer(originalPointer); }
            };
            testTimer.Start();
        }
        else if (warning is not null)
            tray.ShowBalloonTip(7000, "Byte · settings need attention", warning, Forms.ToolTipIcon.Warning);
    }

    private static void Dispatch(Action action) => Application.Current.Dispatcher.Invoke(action);
    private void RefreshMotion()
    {
        var reduced = settings.ReducedMotion || (settings.RespectSystemReducedMotion && Native.SystemReducedMotion());
        bot.ReducedMotion = reduced;
        foreach (var resident in residents) resident.Bot.ReducedMotion = reduced;
    }

    private void WireResident(Companion companion, Overlay window)
    {
        window.Clicked += () => ToggleMenu(companion);
        window.DoubleClicked += () => { menuBot = companion; Interact(Mood.Celebrate); };
        window.DragBegan += () => { social.Cancel(); bubble.Hide(); };
        companion.Speech += text =>
        {
            bubble.Speech(text);
            bubble.Present(companion);
            bubbleUntil = clock.Elapsed.TotalSeconds + 8;
            interactionUntil = bubbleUntil;
        };
    }

    private void SyncFriends(bool gather)
    {
        social.Cancel();
        bubble.Hide();
        menuBot = bot;
        bot.Kind = settings.Character;
        if (!settings.FriendsTogether)
        {
            foreach (var friend in residents.Skip(1))
            {
                friend.Overlay.CancelInteraction();
                friend.Overlay.Close();
            }
            if (residents.Count > 1) residents.RemoveRange(1, residents.Count - 1);
        }
        else
        {
            var kinds = Enum.GetValues<CharacterKind>().Where(k => k != settings.Character).ToArray();
            for (var i = 0; i < kinds.Length; i++)
            {
                if (residents.Count <= i + 1)
                {
                    var companion = new Companion(displays, settings) { Kind = kinds[i], Paused = bot.Paused };
                    if (bot.IsFocusing) companion.StartFocusRemaining(bot.FocusRemaining);
                    var window = new Overlay(companion);
                    WireResident(companion, window);
                    residents.Add((companion, window));
                    window.Show();
                }
                residents[i + 1].Bot.Kind = kinds[i];
            }
        }
        RefreshMotion();
        if (gather && settings.FriendsTogether)
            SocialBehavior.Gather(residents.Select(r => r.Bot).ToArray(), bot.Feet, displays);
        foreach (var resident in residents)
        {
            resident.Overlay.InvalidatePlacement();
            if (!hidden && !fullscreen && !locked && !suspended)
            {
                resident.Overlay.Show();
                resident.Overlay.RenderPosition();
            }
            else resident.Overlay.Hide();
        }
    }

    private void HideResidents()
    {
        social.Cancel();
        bubble.Hide();
        foreach (var resident in residents)
        {
            if (resident.Overlay.IsVisible) resident.Overlay.CancelInteraction();
            resident.Overlay.Hide();
        }
    }
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
            if (wparam.ToInt32() == 4) { suspended = true; HideResidents(); }
            else if (wparam.ToInt32() is 7 or 18)
            {
                suspended = false;
                previous = clock.Elapsed.TotalSeconds;
                displayTimer.Start();
            }
        }
        else if (msg == 0x02B1)
        {
            if (wparam.ToInt32() == 7) { locked = true; HideResidents(); }
            else if (wparam.ToInt32() == 8) { locked = false; displayTimer.Start(); }
        }
    }

    private void RecoverDisplays()
    {
        displays = Native.Displays();
        social.Cancel();
        foreach (var resident in residents)
        {
            resident.Overlay.CancelInteraction();
            resident.Bot.UpdateDisplays(displays);
            resident.Overlay.InvalidatePlacement();
        }
        RefreshMotion();
        bubble.Hide();
        if (settings.FriendsTogether) SocialBehavior.Gather(residents.Select(r => r.Bot).ToArray(), bot.Feet, displays);
        if (!hidden && !fullscreen && !locked && !suspended)
            foreach (var resident in residents) { resident.Overlay.Show(); resident.Overlay.RenderPosition(); }
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        var elapsed = now - previous;
        previous = now;
        var pointer = Native.Cursor();
        foreach (var resident in residents) resident.Overlay.CheckDragRelease();
        var mouseDown = Native.LeftMouseDown;
        if (bubble.IsVisible && mouseDown && !lastMouseDown && !bubble.ContainsScreen(pointer))
        {
            if (!residents.Any(r => r.Overlay.IsVisible &&
                r.Overlay.Character.Interactive(r.Overlay.Character.PointFromScreen(new Point(pointer.X, pointer.Y)))))
                bubble.Hide();
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
                HideResidents();
            }
            else foreach (var resident in residents)
                if (!resident.Overlay.IsVisible) { resident.Overlay.Show(); resident.Overlay.InvalidatePlacement(); }
        }
        var busy = now < busyUntil && now >= interactionUntil;
        var invisible = hidden || fullscreen || suspended || locked;
        social.Tick(elapsed, residents.Select(r => r.Bot).ToArray(), settings,
            invisible || busy || bubble.IsVisible || preferences is not null);
        foreach (var resident in residents)
        {
            var companion = resident.Bot;
            companion.PointerMoved(busy);
            var paused = companion.Paused;
            companion.Paused = paused || invisible || bubble.IsVisible || preferences is not null;
            companion.Tick(elapsed, pointer, busy);
            companion.Paused = paused;
            if (!invisible) resident.Overlay.RenderPosition();
        }
        if (bubble.IsVisible && now >= bubbleUntil) bubble.Hide();
        var animated = residents.Any(r => r.Bot.Mood is not (Mood.Idle or Mood.Sleep or Mood.Sit or Mood.Focus));
        timer.Interval = TimeSpan.FromMilliseconds(hidden || fullscreen || suspended || locked ? 250 :
            bubble.IsVisible ? 16 : bot.ReducedMotion || bot.Paused ? 200 : animated ? 16 : 100);
    }

    private void ToggleMenu(Companion companion)
    {
        if (bubble.IsVisible && menuBot == companion) { bubble.Hide(); return; }
        social.Cancel();
        menuBot = companion;
        bubble.Menu(companion, Interact, companion.TellJoke, ToggleFocus, ToggleHide, OpenSettings, NextMonitor,
            () => SelectCharacter((CharacterKind)(((int)settings.Character + 1) % Enum.GetValues<CharacterKind>().Length)),
            ToggleFriends);
        bubble.Present(companion);
        bubbleUntil = clock.Elapsed.TotalSeconds + 18;
    }

    private void Interact(Mood mood)
    {
        bubble.Hide();
        social.Cancel();
        menuBot.Act(mood);
        interactionUntil = clock.Elapsed.TotalSeconds + 6;
        if (!settings.Muted && mood == Mood.Celebrate) System.Media.SystemSounds.Asterisk.Play();
    }
    private void NextMonitor()
    {
        bubble.Hide();
        if (displays.Count == 1)
        {
            bubble.Speech("One screen, one tiny kingdom.");
            bubble.Present(menuBot);
            bubbleUntil = clock.Elapsed.TotalSeconds + 4;
            return;
        }
        social.Cancel();
        var index = displays.FindIndex(d => d.Id == menuBot.CurrentDisplay.Id);
        menuBot.JumpTo(displays[(index + 1) % displays.Count]);
        interactionUntil = clock.Elapsed.TotalSeconds + 4;
    }
    private void ToggleHide()
    {
        hidden = !hidden;
        HideResidents();
        if (!hidden && !fullscreen && !locked && !suspended)
            foreach (var resident in residents) { resident.Overlay.Show(); resident.Overlay.RenderPosition(); }
        BuildTray();
    }

    private async Task RunFriendsSmoke(string output)
    {
        var checks = new List<string>();
        void Check(bool valid, string label)
        {
            if (!valid) throw new InvalidOperationException("Friends smoke check failed: " + label);
            checks.Add(label);
        }
        settings.FriendsTogether = true;
        settings.Character = CharacterKind.Byte;
        SyncFriends(true);
        Check(residents.Count == 3 && residents.Select(r => r.Bot.Kind).Distinct().Count() == 3,
            "Together mode creates three distinct native companions");
        var foreground = Native.GetForegroundWindow();
        foreach (var resident in residents)
        {
            resident.Bot.Paused = true;
            resident.Overlay.RenderPosition();
        }
        await Task.Delay(250);
        var frames = new System.Windows.Media.DrawingVisual();
        using (var context = frames.RenderOpen())
        {
            for (var i = 0; i < residents.Count; i++)
            {
                var resident = residents[i];
                var center = resident.Overlay.Character.PointToScreen(new Point(77, 88));
                var outside = resident.Overlay.Character.PointToScreen(new Point(4, 4));
                Check(Native.WindowFromPoint(new Native.Point { X = (int)center.X, Y = (int)center.Y }) == resident.Overlay.Handle,
                    resident.Bot.Kind + " has its own interactive HWND");
                Check(Native.WindowFromPoint(new Native.Point { X = (int)outside.X, Y = (int)outside.Y }) != resident.Overlay.Handle,
                    resident.Bot.Kind + " transparent padding is click-through");
                resident.Overlay.Character.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(154, 170, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                image.Render(resident.Overlay.Character);
                context.DrawImage(image, new Rect(i * 174 + 10, 10, 154, 170));
            }
        }
        var family = new System.Windows.Media.Imaging.RenderTargetBitmap(532, 190, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        family.Render(frames);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(family));
        using (var file = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "Byte-friends.png"))) encoder.Save(file);
        foreach (var resident in residents.ToArray())
        {
            bubble.Hide();
            await Task.Delay(650);
            await SmokeTests.Click(resident.Overlay.Character.PointToScreen(new Point(77, 88)));
            await Task.Delay(Forms.SystemInformation.DoubleClickTime + 150);
            Check(bubble.IsVisible && menuBot == resident.Bot, resident.Bot.Kind + " opens its own interaction menu");
            bubble.Hide();
        }
        Check(Native.GetForegroundWindow() == foreground, "Friend windows and menus do not steal focus");
        ToggleHide();
        Check(residents.All(r => !r.Overlay.IsVisible), "Quick hide hides every friend");
        Summon();
        Check(residents.All(r => r.Overlay.IsVisible), "Summon restores every friend");
        Check(residents.Select(r => r.Bot.Feet.X).Distinct().Count() == 3, "Group summon spaces friends apart");
        ToggleFocus();
        Check(residents.All(r => r.Bot.IsFocusing), "Focus suppresses every friend");
        SelectCharacter(CharacterKind.Pip);
        Check(bot.Kind == CharacterKind.Pip && residents.Select(r => r.Bot.Kind).Distinct().Count() == 3 &&
            residents.All(r => r.Bot.IsFocusing), "Character switching preserves shared focus and unique identities");
        ToggleFriends();
        Check(residents.Count == 1 && bot.Kind == CharacterKind.Pip, "Solo mode keeps the selected character");
        ToggleFriends();
        Check(residents.Count == 3 && residents.All(r => r.Bot.IsFocusing), "New friends inherit the running focus timer");
        ToggleFocus();
        settings.AutoInteractions = true;
        settings.Activity = ActivityLevel.Playful;
        foreach (var resident in residents)
        {
            resident.Bot.Paused = false;
            resident.Bot.ReducedMotion = false;
            resident.Bot.Act(Mood.Idle);
        }
        var group = residents.Select(r => r.Bot).ToArray();
        timer.Stop();
        for (var i = 0; i < 1700 && !social.Active; i++) social.Tick(.05, group, settings, false);
        Check(social.Active, "Automatic friendship choreography starts for nearby companions");
        for (var i = 0; i < 130; i++)
        {
            social.Tick(.05, group, settings, false);
            foreach (var resident in residents)
            {
                resident.Bot.Tick(.05, default, false);
                resident.Overlay.RenderPosition();
            }
            await Task.Delay(1);
        }
        social.Tick(.05, group, settings, true);
        Check(!social.Active && group.All(c => !c.IsSocial), "Busy activity cancels every participant immediately");
        settings.FriendsTogether = false;
        SyncFriends(false);
        var report = JsonNode.Parse(System.IO.File.ReadAllText(output))!.AsObject();
        var reportChecks = report["checks"]!.AsArray();
        foreach (var check in checks) reportChecks.Add(check);
        report["friendCharacterCount"] = 3;
        System.IO.File.WriteAllText(output, report.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }));
    }
    private void Summon()
    {
        hidden = false;
        social.Cancel();
        bubble.Hide();
        foreach (var resident in residents) resident.Overlay.CancelInteraction();
        SocialBehavior.Gather(residents.Select(r => r.Bot).ToArray(), Native.Cursor(), displays);
        if (!fullscreen && !locked && !suspended)
            foreach (var resident in residents) { resident.Overlay.Show(); resident.Overlay.RenderPosition(); }
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
        foreach (var resident in residents) resident.Bot.UpdateDisplays(displays);
        SyncFriends(true);
        BuildTray();
    }

    private void SelectCharacter(CharacterKind kind)
    {
        Persist(() => settings.Character = kind);
        SyncFriends(false);
    }
    private void ToggleFriends()
    {
        Persist(() => settings.FriendsTogether = !settings.FriendsTogether);
        SyncFriends(true);
    }
    private void ToggleFocus()
    {
        social.Cancel();
        bubble.Hide();
        var end = residents.Any(r => r.Bot.IsFocusing);
        foreach (var resident in residents)
            if (end) resident.Bot.StopFocus(); else resident.Bot.StartFocus(settings.FocusMinutes);
        BuildTray();
    }
    private void Persist(Action change)
    {
        change();
        try { if (!smoke) storage.Save(settings); }
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
        Add(bot.Paused ? "Resume roaming" : "Pause roaming", () =>
        {
            var pause = !bot.Paused;
            social.Cancel();
            foreach (var resident in residents) resident.Bot.Paused = pause;
            bubble.Hide(); BuildTray();
        }, bot.Paused);
        var characters = new Forms.ToolStripMenuItem("Character");
        foreach (var kind in Enum.GetValues<CharacterKind>())
        {
            var item = new Forms.ToolStripMenuItem(kind switch
            {
                CharacterKind.Pip => "Pip · curious terminal cat",
                CharacterKind.Orbit => "Orbit · tiny space explorer",
                _ => "Byte · original terminal robot"
            }) { Checked = settings.Character == kind };
            item.Click += (_, _) => Dispatch(() => SelectCharacter(kind));
            characters.DropDownItems.Add(item);
        }
        menu.Items.Add(characters);
        Add("Friends together (all three)", ToggleFriends, settings.FriendsTogether);
        Add("Automatic friend interactions", () =>
        {
            Persist(() => settings.AutoInteractions = !settings.AutoInteractions);
            if (!settings.AutoInteractions) social.Cancel();
        }, settings.AutoInteractions);
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var level in Enum.GetValues<ActivityLevel>())
            Add(level.ToString(), () => Persist(() => settings.Activity = level), settings.Activity == level);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(settings.Muted ? "Unmute interaction sounds" : "Mute", () => Persist(() => settings.Muted = !settings.Muted), settings.Muted);
        Add(bot.IsFocusing ? "End focus timer" : "Focus with me", ToggleFocus);
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
        social.Cancel();
        preferences?.Close(); bubble.Close();
        foreach (var resident in residents) resident.Overlay.Close();
    }
}
