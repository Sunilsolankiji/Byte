using System;
using System.Collections.Generic;
using System.Linq;

namespace Byte.Core;

public enum Mood
{
    Idle, Walk, Sit, Peek, Sleep, Windup, Leap, Landing, Drag, Fall,
    Celebrate, Inspect, Sign, Cable, Bug, Warm, Blocks, Focus, Stretch, Trip,
    Approach, Wave, HighFive
}

public sealed class Companion
{
    private readonly Random random;
    private IReadOnlyList<Display> displays;
    private double age;
    private double nextDecision;
    private double facing = 1;
    private PointD velocity;
    private JumpPlan? jump;
    private double focusRemaining;
    private bool dragging;
    private double pointerCooldown;
    private string? lastPointerDisplay;
    private string? pendingDisplay;
    private double socialTarget;

    public Settings Settings { get; }
    public CharacterKind Kind { get; set; }
    public bool IsSocial { get; private set; }
    public bool CanSocialize => !Paused && !ReducedMotion && !IsFocusing && !dragging &&
        (IsSocial || Mood is Mood.Idle or Mood.Walk or Mood.Sit or Mood.Peek);
    public PointD Feet { get; private set; }
    public Display CurrentDisplay { get; private set; }
    public Mood Mood { get; private set; } = Mood.Idle;
    public double StateAge => age;
    public double Facing => facing;
    public double FocusRemaining => focusRemaining;
    public bool IsFocusing => focusRemaining > 0;
    public bool Paused { get; set; }
    public bool ReducedMotion { get; set; }
    public double Opacity => jump?.Portal == true && Mood == Mood.Leap
        ? Math.Abs(age / jump.Duration * 2 - 1) : 1;
    public bool UsingPortal => jump?.Portal == true && (Mood is Mood.Leap or Mood.Windup);
    public event Action<string>? Speech;

    public Companion(IReadOnlyList<Display> displays, Settings settings, int? seed = null)
    {
        if (displays.Count == 0) throw new ArgumentException("At least one display is required.", nameof(displays));
        this.displays = displays;
        Settings = settings;
        Kind = settings.Character;
        random = seed.HasValue ? new Random(seed.Value) : new Random();
        CurrentDisplay = displays.FirstOrDefault(d => d.Primary) ?? displays[0];
        Feet = Desktop.ClampFeet(CurrentDisplay,
            new PointD(CurrentDisplay.Work.Right - 170 * CurrentDisplay.Scale, CurrentDisplay.Work.Bottom), settings.Size);
        nextDecision = 5;
    }

    private void Enter(Mood mood, double duration = 0)
    {
        Mood = mood;
        age = 0;
        nextDecision = duration > 0 ? duration : Interval();
    }

    public void ApproachFriend(double x)
    {
        if (!CanSocialize) return;
        pendingDisplay = null;
        IsSocial = true;
        socialTarget = Desktop.ClampFeet(CurrentDisplay, new PointD(x, CurrentDisplay.Work.Bottom), Settings.Size).X;
        facing = socialTarget < Feet.X ? -1 : 1;
        Enter(Mood.Approach);
    }

    public void GreetFriend(Mood pose, double friendX)
    {
        if (!IsSocial) return;
        facing = friendX < Feet.X ? -1 : 1;
        Enter(pose, 5);
    }

    public void EndSocial()
    {
        if (!IsSocial) return;
        IsSocial = false;
        Enter(IsFocusing ? Mood.Focus : Mood.Idle);
    }

    private double Interval() => Settings.Activity switch
    {
        ActivityLevel.Quiet => 35 + random.NextDouble() * 45,
        ActivityLevel.Playful => 7 + random.NextDouble() * 12,
        _ => 16 + random.NextDouble() * 25
    };

    public void UpdateDisplays(IReadOnlyList<Display> next)
    {
        if (next.Count == 0) return;
        displays = next;
        IsSocial = false;
        var destination = next.FirstOrDefault(d => d.Id == CurrentDisplay.Id) ?? Desktop.Nearest(next, Feet);
        CurrentDisplay = destination;
        Feet = Desktop.ClampFeet(destination, Feet, Settings.Size);
        Feet = new PointD(Feet.X, destination.Work.Bottom);
        velocity = default;
        jump = null;
        pendingDisplay = null;
        dragging = false;
        Enter(IsFocusing ? Mood.Focus : Mood.Idle);
    }

    public void Summon(PointD pointer)
    {
        IsSocial = false;
        CurrentDisplay = Desktop.Nearest(displays, pointer);
        Feet = Desktop.ClampFeet(CurrentDisplay, new PointD(pointer.X, CurrentDisplay.Work.Bottom), Settings.Size);
        velocity = default;
        jump = null;
        pendingDisplay = null;
        dragging = false;
        Enter(IsFocusing ? Mood.Focus : Mood.Idle);
    }

    public void StartDrag()
    {
        IsSocial = false;
        dragging = true;
        jump = null;
        pendingDisplay = null;
        velocity = default;
        Enter(Mood.Drag);
    }

    public void Drag(PointD feet)
    {
        if (!dragging) return;
        CurrentDisplay = Desktop.Nearest(displays, feet);
        Feet = feet;
    }

    public void Release(PointD tossVelocity)
    {
        if (!dragging) return;
        dragging = false;
        Feet = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
        var speed = tossVelocity.Length;
        velocity = speed > 1400 ? tossVelocity * (1400 / speed) : tossVelocity;
        if (ReducedMotion) velocity = default;
        Enter(Mood.Fall);
    }

    public void CancelDrag()
    {
        IsSocial = false;
        dragging = false;
        pendingDisplay = null;
        jump = null;
        velocity = default;
        Feet = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
        Feet = new PointD(Feet.X, CurrentDisplay.Work.Bottom);
        Enter(IsFocusing ? Mood.Focus : Mood.Idle);
    }

    public void Act(Mood mood)
    {
        if (dragging) return;
        IsSocial = false;
        jump = null;
        pendingDisplay = null;
        velocity = default;
        Feet = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
        Feet = new PointD(Feet.X, CurrentDisplay.Work.Bottom);
        Enter(mood, mood == Mood.Sleep ? 180 : 4);
    }

    public void StartFocus(int minutes)
    {
        StartFocusRemaining(Math.Clamp(minutes, 1, 180) * 60);
    }

    public void StartFocusRemaining(double seconds)
    {
        focusRemaining = Math.Clamp(seconds, 0, 180 * 60);
        Act(Mood.Focus);
    }

    public void StopFocus()
    {
        focusRemaining = 0;
        Act(Mood.Stretch);
    }

    public void JumpTo(Display target)
    {
        if (target.Id == CurrentDisplay.Id || dragging || IsFocusing || Paused) return;
        target = displays.FirstOrDefault(d => d.Id == target.Id)
            ?? throw new ArgumentException("The destination monitor is no longer connected.", nameof(target));
        var direction = target.Work.Center.X >= CurrentDisplay.Work.Center.X ? 1 : -1;
        var edge = Desktop.ClampFeet(CurrentDisplay,
            new PointD(direction > 0 ? CurrentDisplay.Work.Right : CurrentDisplay.Work.Left, CurrentDisplay.Work.Bottom), Settings.Size);
        if (!ReducedMotion && Math.Abs(Feet.X - edge.X) > 24 * CurrentDisplay.Scale)
        {
            pendingDisplay = target.Id;
            facing = direction;
            Feet = new PointD(Feet.X, CurrentDisplay.Work.Bottom);
            Enter(Mood.Walk);
            return;
        }
        pendingDisplay = null;
        jump = Desktop.PlanJump(CurrentDisplay, target, Feet, Settings.Size);
        Enter(Mood.Windup, .65);
    }

    public void Tick(double elapsed, PointD pointer, bool busy)
    {
        var dt = Math.Clamp(elapsed, 0, .05);
        if (focusRemaining > 0)
        {
            focusRemaining = Math.Max(0, focusRemaining - Math.Max(0, elapsed));
            if (focusRemaining == 0 && !dragging) Enter(Mood.Stretch, 5);
        }
        if (dragging || Paused) return;
        if (busy && Mood is not (Mood.Leap or Mood.Windup or Mood.Fall or Mood.Landing))
        {
            IsSocial = false;
            pendingDisplay = null;
            if (Mood is not (Mood.Focus or Mood.Sleep or Mood.Sit)) Enter(IsFocusing ? Mood.Focus : Mood.Sit);
            return;
        }
        age += Math.Clamp(elapsed, 0, .25);
        pointerCooldown = Math.Max(0, pointerCooldown - Math.Max(0, elapsed));
        var scale = CurrentDisplay.Scale * Settings.Size;
        if (Mood == Mood.Approach && IsSocial)
        {
            var delta = socialTarget - Feet.X;
            Feet = new PointD(Feet.X + Math.Sign(delta) * Math.Min(Math.Abs(delta), 85 * scale * dt), CurrentDisplay.Work.Bottom);
            if (Math.Abs(delta) < 2 * scale) Enter(Mood.Idle);
        }
        else if (Mood == Mood.Windup && age >= .65 && jump is not null)
            Enter(Mood.Leap);
        else if (Mood == Mood.Leap && jump is not null)
        {
            Feet = Desktop.JumpPosition(jump, age / jump.Duration, ReducedMotion);
            if (age >= jump.Duration)
            {
                CurrentDisplay = displays.FirstOrDefault(d => d.Id == jump.TargetId) ?? Desktop.Nearest(displays, Feet);
                Feet = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
                jump = null;
                Enter(Mood.Landing, .45);
            }
        }
        else if (Mood == Mood.Fall)
        {
            velocity += new PointD(0, 1800 * scale * dt);
            Feet += velocity * dt;
            var safe = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
            if (Feet.X != safe.X) velocity = new PointD(-velocity.X * .25, velocity.Y);
            Feet = new PointD(safe.X, Math.Max(safe.Y, Feet.Y));
            if (Feet.Y >= CurrentDisplay.Work.Bottom)
            {
                Feet = new PointD(safe.X, CurrentDisplay.Work.Bottom);
                velocity = default;
                Enter(Mood.Landing, .45);
            }
            else Feet = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
        }
        else if (Mood == Mood.Walk && !ReducedMotion)
        {
            Feet += new PointD(facing * 42 * scale * dt, 0);
            var safe = Desktop.ClampFeet(CurrentDisplay, Feet, Settings.Size);
            if (safe.X != Feet.X)
            {
                Feet = safe;
                if (pendingDisplay is not null)
                {
                    var target = displays.FirstOrDefault(d => d.Id == pendingDisplay);
                    pendingDisplay = null;
                    if (target is not null) { JumpTo(target); return; }
                }
                facing *= -1;
                Enter(random.Next(2) == 0 ? Mood.Peek : Mood.Sit, 6);
            }
        }
        else if (Mood == Mood.Inspect && !ReducedMotion && age < 1.5)
        {
            var delta = pointer.X - Feet.X;
            if (Math.Abs(delta) > 60 * scale)
                Feet = Desktop.ClampFeet(CurrentDisplay, Feet + new PointD(Math.Sign(delta) * 36 * scale * dt, 0), Settings.Size);
        }
        else if (Mood == Mood.Bug && !ReducedMotion)
            Feet = Desktop.ClampFeet(CurrentDisplay, Feet + new PointD(Math.Sin(age * 3) * 25 * scale * dt, 0), Settings.Size);

        if (Mood is Mood.Leap or Mood.Windup or Mood.Fall) return;
        if (IsSocial) return;
        if (pendingDisplay is not null) return;
        if (IsFocusing)
        {
            if (Mood != Mood.Focus) Enter(Mood.Focus);
            return;
        }
        if (age < nextDecision) return;
        if (Mood != Mood.Idle)
        {
            Enter(Mood.Idle);
            return;
        }
        if (ReducedMotion || Settings.Activity == ActivityLevel.Quiet)
        {
            Enter(random.Next(3) == 0 ? Mood.Sleep : Mood.Sit);
            return;
        }
        var choice = random.Next(100);
        if (displays.Count > 1 && choice < 13)
        {
            var others = displays.Where(d => d.Id != CurrentDisplay.Id).ToArray();
            JumpTo(others[random.Next(others.Length)]);
        }
        else if (choice < 48)
        {
            facing = random.Next(2) == 0 ? -1 : 1;
            Feet = new PointD(Feet.X, CurrentDisplay.Work.Bottom);
            Enter(Mood.Walk, 3 + random.Next(6));
        }
        else if (choice < 60) Enter(Mood.Sleep, 35);
        else if (choice < 72) Enter(Mood.Sit, 12);
        else if (choice < 77) Enter(Mood.Trip, 2);
        else if (Settings.PointerPlay && pointerCooldown == 0 &&
                 CurrentDisplay.Work.Contains(pointer) && (pointer - Feet).Length < 320 * scale)
        {
            // A short, bounded pursuit; never follow the pointer across the user's work.
            lastPointerDisplay = CurrentDisplay.Id;
            pointerCooldown = 120;
            facing = pointer.X < Feet.X ? -1 : 1;
            Enter(Mood.Inspect, 5);
        }
        else Enter(new[] { Mood.Sign, Mood.Cable, Mood.Bug, Mood.Warm, Mood.Blocks }[random.Next(5)], 6);
    }

    public void PointerMoved(bool busy)
    {
        if ((busy || lastPointerDisplay != CurrentDisplay.Id) && Mood == Mood.Inspect) Enter(Mood.Idle);
    }

    public void TellJoke()
    {
        var jokes = new[]
        {
            "It works on my machine.\nMy machine is a tiny face.",
            "There are two hard things:\ncache invalidation, naming,\nand off-by-one errors.",
            "I tried to catch a bug.\nIt filed a feature request.",
            "Your code has character.\nMostly semicolons.",
            "A SQL query walks into a bar,\nsees two tables, and asks:\nCan I join you?",
            "I'd tell you a UDP joke,\nbut you might not get it.",
            "My sleep schedule?\nA very optimistic cron job.",
            "Hello, world!\nNo, not the production one."
        };
        var index = lastJoke < 0 ? random.Next(jokes.Length) : random.Next(jokes.Length - 1);
        if (lastJoke >= 0 && index >= lastJoke) index++;
        lastJoke = index;
        Speech?.Invoke(jokes[index]);
        Act(Mood.Sign);
    }

    private int lastJoke = -1;
}
