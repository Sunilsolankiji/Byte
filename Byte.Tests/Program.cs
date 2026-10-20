using System;
using System.Linq;
using System.Text.Json;
using Byte.Core;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    count++;
}

var main = new Display("main", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1, true);
var left = new Display("left", new(-2560, -400, 0, 1040), new(-2560, -400, 0, 1000), 1.5);
var remote = new Display("remote", new(4500, -1800, 8340, 360), new(4500, -1800, 8340, 320), 2);
var above = new Display("above", new(0, -1080, 1920, 0), new(0, -1080, 1920, 0), 1);
var displays = new[] { main, left, remote, above };
foreach (var d in displays)
foreach (var size in new[] { .65, 1.0, 1.6 })
foreach (var point in new[] { new PointD(-99999, -99999), new PointD(99999, 99999), d.Work.Center })
{
    var feet = Desktop.ClampFeet(d, point, size);
    Check(d.Work.Contains(feet) &&
          feet.X - Desktop.SpriteWidth * d.Scale * size / 2 >= d.Work.Left &&
          feet.X + Desktop.SpriteWidth * d.Scale * size / 2 <= d.Work.Right &&
          feet.Y - Desktop.SpriteHeight * d.Scale * size >= d.Work.Top, $"clamp {d.Id} {size} {point}");
}
Check(Desktop.Nearest(displays, new(-2300, 0)).Id == "left", "negative-coordinate monitor selection");
Check(Desktop.Nearest(displays, new(4300, -500)).Id == "remote", "gap chooses actual nearest monitor");
var natural = Desktop.PlanJump(main, left, new(90, 1040), 1);
Check(!natural.Portal, "nearby displays use natural leap");
Check(Desktop.JumpPosition(natural, .5, false).Y < (natural.Start.Y + natural.End.Y) / 2, "leap arc rises");
Check(Desktop.JumpPosition(natural, 1, false) == natural.End, "leap ends exactly on target");
var portal = Desktop.PlanJump(main, remote, new(1800, 1040), 1);
Check(portal.Portal, "large gap uses portal");
Check(Desktop.JumpPosition(portal, .49, false) == portal.Start &&
      Desktop.JumpPosition(portal, .51, false) == portal.End, "portal does not traverse invisible gap");
Check(Desktop.PlanJump(main, above, new(1800, 1040), 1).Portal, "large vertical transition uses portal");
var settings = new Settings();
var bot = new Companion(displays, settings, 42);
bot.Summon(new PointD(90, 0));
bot.JumpTo(left);
Check(bot.Mood == Mood.Windup, "visible jump windup");
for (var i = 0; i < 41; i++) bot.Tick(.05, default, false);
Check(bot.CurrentDisplay.Id == "left" && left.Work.Contains(bot.Feet), "mixed-DPI leap lands safely");
bot.JumpTo(remote);
bot.UpdateDisplays(new[] { main });
Check(bot.CurrentDisplay.Id == "main" && main.Work.Contains(bot.Feet), "disconnect mid-jump recovers");
bot.StartDrag();
bot.Drag(new(-50000, 20000));
bot.Release(new(20000, -20000));
for (var i = 0; i < 200; i++) bot.Tick(.05, default, false);
Check(main.Work.Contains(bot.Feet), "extreme toss safely lands");
bot.StartDrag();
bot.Drag(new(900, 500));
bot.Release(new(300, -400));
for (var i = 0; i < 100; i++) bot.Tick(.05, default, false);
Check(Math.Abs(bot.Feet.Y - main.Work.Bottom) < .1, "gravity reaches floor");
bot.StartFocus(1);
bot.Tick(59, default, false);
Check(bot.IsFocusing, "focus uses wall time rather than capped physics delta");
bot.Tick(1, default, false);
Check(!bot.IsFocusing && bot.Mood == Mood.Stretch, "focus ends with stretch");
bot.Act(Mood.Celebrate);
bot.Tick(.02, default, true);
Check(bot.Mood == Mood.Sit, "busy input cancels playful behavior immediately");
bot.ReducedMotion = true;
bot.StartDrag();
bot.Drag(new(600, 700));
bot.Release(new(1000, -1000));
var x = bot.Feet.X;
for (var i = 0; i < 50; i++) bot.Tick(.05, default, false);
Check(bot.Feet.X == x, "reduced motion suppresses tossing");
bot.UpdateDisplays(new[] { main with { Scale = 2, Work = new(0, 0, 1920, 1000) } });
Check(bot.Feet.Y <= 1000 && bot.CurrentDisplay.Scale == 2, "DPI/work-area change recovers");
var random = new Random(91);
for (var i = 0; i < 4000; i++)
{
    var d = displays[random.Next(displays.Length)];
    var feet = Desktop.ClampFeet(d, new(random.Next(-10000, 10000), random.Next(-10000, 10000)), .65 + random.NextDouble() * .95);
    if (!d.Work.Contains(feet)) throw new Exception("Randomized geometry failed");
}
Check(true, "4000 randomized virtual-desktop placements");
settings.Size = double.NaN;
settings.FocusMinutes = -1;
settings.Validate();
Check(settings.Size == 1 && settings.FocusMinutes == 1, "invalid local settings sanitized");
var approach = new Companion(new[] { main, left }, new Settings(), 4);
approach.JumpTo(left);
Check(approach.Mood == Mood.Walk, "cross-monitor travel approaches the shared edge before jumping");
for (var i = 0; i < 1400; i++) approach.Tick(.05, default, false);
Check(approach.CurrentDisplay.Id == "left", "edge approach completes a natural monitor crossing");
approach.StartDrag();
approach.Drag(new(-300, 500));
approach.CancelDrag();
Check(approach.Feet.Y == left.Work.Bottom, "lost drag capture recovers to a safe floor");
approach.Summon(new PointD(1700, 0));
approach.JumpTo(left);
approach.Tick(.05, default, true);
for (var i = 0; i < 1600; i++) approach.Tick(.05, default, false);
Check(approach.Mood != Mood.Sit || approach.StateAge < 80, "busy input cancels pending travel without wedging behavior");
var legacy = JsonSerializer.Deserialize<Settings>("{\"Size\":1.2,\"Muted\":true}")!;
Check(legacy.Character == CharacterKind.Byte && !legacy.FriendsTogether && legacy.AutoInteractions,
    "old settings migrate to solo Byte without losing privacy defaults");
legacy.Character = (CharacterKind)999;
legacy.Validate();
Check(legacy.Character == CharacterKind.Byte, "invalid character setting recovers safely");
var groupSettings = new Settings { FriendsTogether = true, AutoInteractions = true };
var group = Enum.GetValues<CharacterKind>().Select((kind, i) => new Companion(displays, groupSettings, i + 10) { Kind = kind }).ToArray();
SocialBehavior.Gather(group, new(-120, 100), displays);
Check(group.All(c => c.CurrentDisplay.Id == "left" && left.Work.Contains(c.Feet)) &&
    group.Select(c => c.Feet.X).Distinct().Count() == 3, "group summon spaces distinct characters on negative mixed-DPI monitor");
for (var i = 0; i < group.Length - 1; i++)
    Check(group[i + 1].Feet.X - group[i].Feet.X >= Desktop.SpriteWidth * left.Scale,
        "formation leaves each character separately clickable");
var social = new SocialBehavior(7);
for (var i = 0; i < 350 && !social.Active; i++) social.Tick(.05, group, groupSettings, false);
Check(social.Active && group.Count(c => c.IsSocial) == 2, "automatic interactions choose exactly two nearby friends");
var seenScene = false;
for (var i = 0; i < 140; i++)
{
    social.Tick(.05, group, groupSettings, false);
    foreach (var c in group) c.Tick(.05, default, false);
    seenScene |= group.Any(c => c.IsSocial && c.Mood == Mood.HighFive);
}
Check(seenScene, "friends walk toward one another and perform a coordinated high-five");
Check(group.All(c => left.Work.Contains(c.Feet)), "social choreography never leaves real monitor work area");
social.Tick(.05, group, groupSettings, true);
Check(!social.Active && group.All(c => !c.IsSocial), "busy input immediately cancels a social scene");
void ReadyScene()
{
    social.Cancel();
    foreach (var c in group) { c.Paused = false; c.ReducedMotion = false; if (c.IsFocusing) c.StopFocus(); c.Act(Mood.Idle); }
    SocialBehavior.Gather(group, new(-1000, 0), displays);
    groupSettings.Activity = ActivityLevel.Balanced;
    groupSettings.FriendsTogether = true;
    groupSettings.AutoInteractions = true;
}
ReadyScene();
group[0].StartFocus(25);
for (var i = 0; i < 1000; i++) social.Tick(.05, group, groupSettings, false);
Check(!social.Active && group.All(c => !c.IsSocial), "focus on any companion suppresses all social scenes");
ReadyScene();
group[1].ReducedMotion = true;
for (var i = 0; i < 1000; i++) social.Tick(.05, group, groupSettings, false);
Check(!social.Active, "reduced motion disables social movement");
ReadyScene();
groupSettings.AutoInteractions = false;
for (var i = 0; i < 1000; i++) social.Tick(.05, group, groupSettings, false);
Check(!social.Active, "automatic interactions can be disabled independently of group mode");
ReadyScene();
groupSettings.Activity = ActivityLevel.Quiet;
for (var i = 0; i < 1000; i++) social.Tick(.05, group, groupSettings, false);
Check(!social.Active, "Quiet mode does not initiate friend interactions");
ReadyScene();
for (var i = 0; i < 500 && !social.Active; i++) social.Tick(.05, group, groupSettings, false);
var dragged = group.First(c => c.IsSocial);
dragged.StartDrag();
social.Tick(.05, group, groupSettings, false);
Check(!social.Active && dragged.Mood == Mood.Drag && group.All(c => !c.IsSocial), "dragging cancels a scene without taking over the dragged friend");
dragged.CancelDrag();
ReadyScene();
for (var i = 0; i < 500 && !social.Active; i++) social.Tick(.05, group, groupSettings, false);
var removed = group.First(c => c.IsSocial);
removed.UpdateDisplays(new[] { main });
social.Tick(.05, group, groupSettings, false);
Check(!social.Active && main.Work.Contains(removed.Feet), "display disconnection interrupts social choreography safely");
ReadyScene();
group[0].StartFocusRemaining(37.5);
var joiner = new Companion(displays, groupSettings);
joiner.StartFocusRemaining(group[0].FocusRemaining);
Check(joiner.FocusRemaining == 37.5, "newly shown friends inherit exact remaining focus time");
groupSettings.Character = CharacterKind.Orbit;
var roundTrip = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(groupSettings))!;
Check(roundTrip.Character == CharacterKind.Orbit && roundTrip.FriendsTogether && roundTrip.AutoInteractions,
    "selected character, together mode and automatic interactions persist");
var small = new Display("small", new(0, 0, 340, 600), new(0, 0, 340, 560), 1.5);
foreach (var c in group) c.UpdateDisplays(new[] { small });
SocialBehavior.Gather(group, new(10000, 10000), new[] { small });
Check(group.All(c => small.Work.Contains(c.Feet)), "group summon clamps safely on a narrow high-DPI work area");
Console.WriteLine($"{count} checks passed.");
