using System;
using System.Collections.Generic;
using System.Linq;

namespace Byte.Core;

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD a, double b) => new(a.X * b, a.Y * b);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public readonly record struct RectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public PointD Center => new((Left + Right) / 2, (Top + Bottom) / 2);
    public bool Contains(PointD p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
}

public sealed record Display(string Id, RectD Bounds, RectD Work, double Scale, bool Primary = false);
public sealed record JumpPlan(PointD Start, PointD End, string TargetId, bool Portal, double Duration, double Height);

public static class Desktop
{
    public const double SpriteWidth = 154;
    public const double SpriteHeight = 170;

    public static Display Nearest(IReadOnlyList<Display> displays, PointD point) =>
        displays.OrderBy(d => Distance(d.Bounds, point)).ThenByDescending(d => d.Primary).First();

    public static double Distance(RectD rect, PointD point)
    {
        var dx = Math.Max(rect.Left - point.X, Math.Max(0, point.X - rect.Right));
        var dy = Math.Max(rect.Top - point.Y, Math.Max(0, point.Y - rect.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // Position is the sprite's bottom-center, in virtual desktop physical pixels.
    public static PointD ClampFeet(Display display, PointD feet, double size)
    {
        var half = Math.Min(display.Work.Width / 2, SpriteWidth * display.Scale * size / 2 + 4);
        var height = Math.Min(display.Work.Height, SpriteHeight * display.Scale * size + 4);
        return new(
            Math.Clamp(feet.X, display.Work.Left + half, display.Work.Right - half),
            Math.Clamp(feet.Y, display.Work.Top + height, display.Work.Bottom));
    }

    public static JumpPlan PlanJump(Display source, Display target, PointD feet, double size)
    {
        var goingRight = target.Work.Center.X >= source.Work.Center.X;
        var half = SpriteWidth * target.Scale * size / 2 + 16;
        var end = ClampFeet(target,
            new PointD(goingRight ? target.Work.Left + half : target.Work.Right - half, target.Work.Bottom), size);
        var distance = (end - feet).Length;
        var portal = distance > 780 * Math.Max(source.Scale, target.Scale) ||
                     Math.Abs(end.Y - feet.Y) > 420 * Math.Max(source.Scale, target.Scale);
        return new(feet, end, target.Id, portal, portal ? 1.1 : 1.05,
            Math.Clamp(distance * .3, 90, 260) * Math.Max(source.Scale, target.Scale));
    }

    public static PointD JumpPosition(JumpPlan plan, double progress, bool reducedMotion)
    {
        var t = Math.Clamp(progress, 0, 1);
        if (plan.Portal) return t < .5 ? plan.Start : plan.End;
        var point = plan.Start * (1 - t) + plan.End * t;
        return reducedMotion ? point : point + new PointD(0, -4 * plan.Height * t * (1 - t));
    }
}
