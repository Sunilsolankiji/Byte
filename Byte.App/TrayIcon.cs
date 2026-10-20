using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Byte.App;

internal static class TrayIcon
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var mint = new SolidBrush(Color.FromArgb(135, 245, 208));
        using var dark = new SolidBrush(Color.FromArgb(19, 43, 54));
        using var gold = new SolidBrush(Color.FromArgb(255, 198, 107));
        using var pen = new Pen(Color.FromArgb(19, 43, 54), 2);
        g.DrawLine(pen, 16, 9, 20, 3);
        g.FillEllipse(gold, 18, 0, 5, 5);
        g.FillRectangle(dark, 8, 25, 4, 6); g.FillRectangle(dark, 22, 25, 4, 6);
        g.FillRoundedRectangle(mint, new Rectangle(3, 8, 27, 19), 5);
        g.FillRoundedRectangle(dark, new Rectangle(6, 11, 21, 12), 3);
        g.FillRectangle(mint, 10, 14, 3, 5); g.FillRectangle(mint, 20, 14, 3, 5);
        var native = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(native).Clone(); }
        finally { DestroyIcon(native); }
    }

    private static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(rect.Left, rect.Top, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Top, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
