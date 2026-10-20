using System;
using System.Collections.Generic;
using System.Linq;

namespace Byte.Core;

public sealed class SocialBehavior
{
    private readonly Random random;
    private Companion? first, second;
    private double cooldown = 15, age;
    private bool performing;
    private int scene;
    public bool Active => first is not null;

    public SocialBehavior(int? seed = null) => random = seed.HasValue ? new Random(seed.Value) : new Random();

    public void Cancel()
    {
        first?.EndSocial();
        second?.EndSocial();
        first = second = null;
        performing = false;
        age = 0;
        cooldown = 12;
    }

    public void Tick(double elapsed, IReadOnlyList<Companion> companions, Settings settings, bool blocked)
    {
        if (blocked || !settings.FriendsTogether || !settings.AutoInteractions ||
            settings.Activity == ActivityLevel.Quiet || companions.Any(c => c.IsFocusing || c.ReducedMotion || c.Paused))
        {
            Cancel();
            return;
        }
        var dt = Math.Clamp(elapsed, 0, .25);
        if (first is not null && second is not null)
        {
            if (!companions.Contains(first) || !companions.Contains(second) ||
                !first.IsSocial || !second.IsSocial || !first.CanSocialize || !second.CanSocialize ||
                first.CurrentDisplay.Id != second.CurrentDisplay.Id)
            {
                Cancel();
                return;
            }
            age += dt;
            if (!performing && first.Mood == Mood.Idle && second.Mood == Mood.Idle)
            {
                performing = true;
                age = 0;
                var poses = scene switch
                {
                    0 => (Mood.Wave, Mood.Wave),
                    1 => (Mood.HighFive, Mood.HighFive),
                    _ => (Mood.Blocks, Mood.Celebrate)
                };
                first.GreetFriend(poses.Item1, second.Feet.X);
                second.GreetFriend(poses.Item2, first.Feet.X);
            }
            if (age >= (performing ? 5 : 9))
            {
                Cancel();
                cooldown = settings.Activity == ActivityLevel.Playful ? 18 + random.Next(12) : 40 + random.Next(25);
            }
            return;
        }
        cooldown -= dt;
        if (cooldown > 0) return;
        var available = companions.Where(c => c.CanSocialize).OrderBy(_ => random.Next()).ToArray();
        foreach (var a in available)
        foreach (var b in available)
        {
            if (ReferenceEquals(a, b) || a.CurrentDisplay.Id != b.CurrentDisplay.Id) continue;
            var scale = a.CurrentDisplay.Scale * settings.Size;
            if (Math.Abs(a.Feet.X - b.Feet.X) > 650 * scale) continue;
            var work = a.CurrentDisplay.Work;
            var half = Desktop.SpriteWidth * scale / 2 + 4;
            var spacing = 148 * scale;
            if (work.Width < spacing + 2 * half) continue;
            var middle = Math.Clamp((a.Feet.X + b.Feet.X) / 2,
                work.Left + half + spacing / 2, work.Right - half - spacing / 2);
            var left = a.Feet.X <= b.Feet.X ? a : b;
            var right = ReferenceEquals(left, a) ? b : a;
            left.ApproachFriend(middle - spacing / 2);
            right.ApproachFriend(middle + spacing / 2);
            first = left;
            second = right;
            scene = (scene + 1) % 3;
            age = 0;
            return;
        }
        cooldown = 8;
    }

    public static void Gather(IReadOnlyList<Companion> companions, PointD pointer, IReadOnlyList<Display> displays)
    {
        if (companions.Count == 0) return;
        var display = Desktop.Nearest(displays, pointer);
        var scale = display.Scale * companions[0].Settings.Size;
        var half = Math.Min(display.Work.Width / 2, Desktop.SpriteWidth * scale / 2 + 4);
        var spacing = companions.Count < 2 ? 0 :
            Math.Min(190 * scale, (display.Work.Width - 2 * half) / (companions.Count - 1));
        var span = spacing * (companions.Count - 1);
        var center = Math.Clamp(pointer.X, display.Work.Left + half + span / 2, display.Work.Right - half - span / 2);
        for (var i = 0; i < companions.Count; i++)
            companions[i].Summon(new PointD(center - span / 2 + i * spacing, display.Work.Center.Y));
    }
}
