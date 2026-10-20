using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Byte.Core;

namespace Byte.App;

internal sealed class Character : FrameworkElement
{
    private static readonly Brush Mint = Solid("#87F5D0"), Teal = Solid("#2CBFA8"), Ink = Solid("#132B36"),
        Screen = Solid("#10272E"), Dim = Solid("#43716C"), Gold = Solid("#FFC66B"),
        White = Solid("#EDFDF5"), Coral = Solid("#FF947D"), Purple = Solid("#B6AAFF");
    private static readonly Pen Outline = new(Ink, 3), Wire = new(Dim, 3), Light = new(Mint, 3);
    private static readonly Brush Shell = Frozen(new LinearGradientBrush(
        Color.FromRgb(224, 251, 232), Color.FromRgb(100, 200, 178), 75));
    private static readonly Brush Glass = Frozen(new LinearGradientBrush(
        Color.FromRgb(26, 63, 68), Color.FromRgb(9, 25, 35), 90));
    private readonly Dictionary<string, FormattedText> textCache = new();
    public Companion Bot { get; }
    public Character(Companion bot) { Bot = bot; Width = 154; Height = 170; Focusable = false; }

    private static Brush Solid(string color) => Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
    private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush)
    {
        var key = text + size.ToString(CultureInfo.InvariantCulture) + brush;
        if (!textCache.TryGetValue(key, out var formatted))
        {
            formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), size, brush, 1);
            if (textCache.Count > 200) textCache.Clear();
            textCache[key] = formatted;
        }
        dc.DrawText(formatted, new Point(x, y));
    }
    private static void Line(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2) =>
        dc.DrawLine(pen, new(x1, y1), new(x2, y2));

    public bool Interactive(Point p) =>
        new Rect(28, 43, 99, 91).Contains(p) ||
        new Rect(10, 76, 134, 49).Contains(p) ||
        new Rect(39, 129, 78, 38).Contains(p) ||
        new Rect(67, 10, 35, 36).Contains(p);

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        Interactive(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    protected override void OnRender(DrawingContext dc)
    {
        var mood = Bot.Mood;
        var t = Bot.StateAge;
        var reduced = Bot.ReducedMotion;
        var resting = mood is Mood.Sleep or Mood.Sit;
        var walk = mood == Mood.Walk;
        var bounce = reduced ? 0 : walk ? Math.Abs(Math.Sin(t * 10)) * -4 : Math.Sin(t * 2) * 1.6;
        var tilt = reduced ? 0 : mood switch
        {
            Mood.Drag => Math.Sin(t * 8) * 5,
            Mood.Trip => Math.Sin(Math.Min(1, t) * Math.PI) * 22,
            Mood.Inspect => -9 * Bot.Facing,
            Mood.Celebrate => Math.Sin(t * 12) * 8,
            Mood.Leap => -8 * Bot.Facing,
            Mood.Peek => 12 * Bot.Facing,
            _ => walk ? Math.Sin(t * 10) * 2 : 0
        };
        var squash = !reduced && mood is Mood.Windup or Mood.Landing ? .86 + Math.Min(t, .5) * .12 : 1;
        var sit = resting ? 11 : 0;
        dc.PushOpacity(Math.Clamp(Bot.Opacity, 0, 1));
        if (mood is not (Mood.Leap or Mood.Drag or Mood.Fall))
        {
            dc.PushOpacity(.14);
            dc.DrawEllipse(Ink, null, new(77, 165), resting ? 44 : 37, 4);
            dc.Pop();
        }
        // Grounded feet are independent from the buoyant terminal body.
        for (var i = 0; i < 2; i++)
        {
            var x = i == 0 ? 55 : 99;
            var stride = !reduced && walk ? Math.Sin(t * 10 + i * Math.PI) * 7 : 0;
            var lifted = mood is Mood.Leap or Mood.Drag ? -10 : Math.Min(0, stride);
            Line(dc, Outline, x, 125 + sit, x + stride, 151 + lifted);
            dc.DrawRoundedRectangle(Ink, null, new(x - 15 + stride, 151 + lifted, 29, 12), 6, 6);
            dc.DrawRoundedRectangle(Teal, null, new(x - 13 + stride, 151 + lifted, 25, 8), 4, 4);
            Line(dc, new Pen(Mint, 1.3), x - 8 + stride, 154 + lifted, x + 5 + stride, 154 + lifted);
        }
        dc.PushTransform(new TranslateTransform(0, bounce + sit));
        dc.PushTransform(new RotateTransform(tilt, 77, 132));
        dc.PushTransform(new ScaleTransform(1 + (1 - squash) * .35, squash, 77, 132));

        var antenna = reduced ? 0 : Math.Sin(t * 3) * 3;
        Line(dc, Wire, 77, 46, 84 + antenna, 32);
        dc.PushOpacity(.15);
        dc.DrawEllipse(Mint, null, new(87 + antenna, 23), 13, 13);
        dc.Pop();
        dc.DrawEllipse(Gold, Outline, new(87 + antenna, 23), 6.5, 6.5);
        dc.DrawEllipse(White, null, new(85 + antenna, 21), 2, 2);
        dc.DrawRoundedRectangle(Ink, null, new(65, 41, 25, 8), 4, 4);

        var handsUp = mood is Mood.Celebrate or Mood.Stretch or Mood.Fall;
        for (var i = 0; i < 2; i++)
        {
            var x = i == 0 ? 28 : 126;
            var endX = i == 0 ? 17 : 137;
            var endY = handsUp ? 68 : 113 + (reduced ? 0 : Math.Sin(t * 4 + i) * 3);
            Line(dc, Outline, x, 97, endX, endY);
            dc.DrawEllipse(Teal, Outline, new(endX, endY), 6, 7);
            Line(dc, new Pen(Mint, 1), endX - 2, endY - 3, endX + 1, endY - 4);
        }
        dc.DrawRoundedRectangle(Shell, Outline, new(29, 47, 96, 86), 17, 17);
        dc.DrawRoundedRectangle(null, new Pen(White, 1), new(33, 50, 88, 77), 14, 14);
        dc.DrawRoundedRectangle(Glass, new Pen(Ink, 2), new(37, 56, 80, 61), 10, 10);
        dc.PushOpacity(.35);
        Line(dc, new Pen(Mint, 1), 46, 61, 105, 61);
        for (var scan = 65; scan < 114; scan += 4) Line(dc, new Pen(Screen, 1), 42, scan, 112, scan);
        dc.Pop();
        var blink = !reduced && (t % 6.3 > 6.13);
        if (mood == Mood.Sleep)
        {
            Line(dc, Light, 49, 83, 62, 83);
            Line(dc, Light, 91, 83, 104, 83);
            dc.DrawEllipse(null, new Pen(Mint, 2), new(77, 99), 4, 5);
        }
        else if (mood == Mood.Celebrate)
        {
            Line(dc, Light, 48, 85, 56, 77); Line(dc, Light, 56, 77, 64, 85);
            Line(dc, Light, 90, 85, 98, 77); Line(dc, Light, 98, 77, 106, 85);
            dc.DrawRoundedRectangle(Mint, null, new(67, 95, 20, 8), 4, 4);
        }
        else
        {
            var eyeHeight = blink ? 2 : mood is Mood.Sit or Mood.Focus ? 8 : 15;
            var offset = mood is Mood.Inspect or Mood.Peek or Mood.Walk ? Bot.Facing * 3 : 0;
            dc.DrawRoundedRectangle(Mint, null, new(50 + offset, 76, 11, eyeHeight), 4, 4);
            dc.DrawRoundedRectangle(Mint, null, new(93 + offset, 76, 11, eyeHeight), 4, 4);
            if (!blink)
            {
                dc.DrawRectangle(White, null, new(52 + offset, 78, 3, 3));
                dc.DrawRectangle(White, null, new(95 + offset, 78, 3, 3));
            }
            if (mood is Mood.Drag or Mood.Trip or Mood.Fall)
                dc.DrawEllipse(null, new Pen(Mint, 2), new(77, 101), 4, 5);
            else
            {
                Line(dc, new Pen(Mint, 2), 70, 99, 76, 102);
                Line(dc, new Pen(Mint, 2), 76, 102, 84, 98);
            }
        }
        dc.DrawEllipse(Gold, null, new(45, 124), 2, 2);
        Text(dc, ">_", 68, 119, 8, Ink);
        for (var i = 0; i < 3; i++) Line(dc, new Pen(Dim, 1), 100 + i * 4, 122, 100 + i * 4, 126);
        if (Bot.IsFocusing || mood == Mood.Focus)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new(27, 80), false, false);
                ctx.BezierTo(new(25, 24), new(130, 24), new(127, 80), true, false);
            }
            dc.DrawGeometry(null, new Pen(Purple, 5), geometry);
            dc.DrawRoundedRectangle(Purple, Outline, new(23, 73, 12, 28), 5, 5);
            dc.DrawRoundedRectangle(Purple, Outline, new(120, 73, 12, 28), 5, 5);
        }
        dc.Pop(); dc.Pop(); dc.Pop();

        if (mood == Mood.Sleep) Text(dc, "z", 117, 29 - (reduced ? 0 : t % 3 * 4), 16, Mint);
        if (mood == Mood.Sign)
        {
            Line(dc, Wire, 128, 117, 128, 82);
            dc.DrawRoundedRectangle(Gold, Outline, new(88, 64, 62, 27), 4, 4);
            Text(dc, "Hello,", 94, 66, 10, Ink); Text(dc, "world!", 101, 77, 10, Ink);
        }
        if (mood == Mood.Inspect)
        {
            Line(dc, new Pen(Gold, 5), 119, 113, 129, 94);
            dc.DrawEllipse(null, new Pen(Gold, 4), new(133, 86), 12, 12);
            Line(dc, new Pen(White, 2), 129, 80, 135, 77);
        }
        if (mood == Mood.Cable)
        {
            var curve = new StreamGeometry();
            using (var ctx = curve.Open())
            {
                ctx.BeginFigure(new(13, 132), false, false);
                ctx.BezierTo(new(80, 97), new(0, 170), new(45, 153), true, false);
                ctx.BezierTo(new(100, 115), new(50, 169), new(133, 141), true, false);
            }
            dc.DrawGeometry(null, new Pen(Purple, 3), curve);
            dc.DrawRectangle(Gold, null, new(128, 137, 12, 7));
        }
        if (mood == Mood.Bug)
        {
            var x = 28 + (reduced ? 0 : Math.Sin(t * 4) * 16);
            for (var i = 0; i < 3; i++) Line(dc, new Pen(Coral, 1.5), x - 8, 145 + i * 4, x + 8, 147 + i * 4);
            dc.DrawEllipse(Coral, Outline, new(x, 151), 5, 7);
        }
        if (mood == Mood.Warm)
        {
            dc.DrawRoundedRectangle(Gold, Outline, new(112, 139, 28, 22), 3, 3);
            Text(dc, "CPU", 116, 145, 10, Ink);
            for (var i = 0; i < 3; i++)
            {
                Line(dc, Wire, 115 + i * 9, 135, 115 + i * 9, 139);
                if (!reduced) Text(dc, "~", 112 + i * 9, 121 - t % 1 * 5, 12, Coral);
            }
        }
        if (mood == Mood.Blocks)
        {
            var collapse = t > 3.5 && !reduced;
            for (var i = 0; i < 3; i++)
            {
                var y = collapse ? 150 : 147 - i * 16;
                var x = collapse ? 105 + i * 12 : 120;
                dc.PushTransform(new RotateTransform(collapse ? i * 22 : 0, x + 10, y + 7));
                dc.DrawRoundedRectangle(i == 1 ? Gold : Purple, Outline, new(x, y, 23, 15), 3, 3);
                Text(dc, i == 1 ? "{}" : "<>", x + 4, y + 1, 10, Ink);
                dc.Pop();
            }
        }
        if (mood == Mood.Celebrate && !reduced)
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4 + t;
                var radius = 35 + (t * 28 + i * 8) % 35;
                dc.DrawRectangle(i % 2 == 0 ? Gold : Purple, null,
                    new(77 + Math.Cos(angle) * radius, 65 + Math.Sin(angle) * 45, 4, 7));
            }
        }
        if (Bot.UsingPortal && !reduced)
        {
            dc.PushOpacity(.7);
            dc.DrawEllipse(null, new Pen(Purple, 5), new(77, 150), 47, 11);
            dc.DrawEllipse(null, new Pen(Mint, 2), new(77, 150), 39, 7);
            dc.Pop();
        }
        dc.Pop();
    }
}
