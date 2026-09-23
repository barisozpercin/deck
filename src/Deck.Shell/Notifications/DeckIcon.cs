using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Deck.Shell.Notifications;

/// <summary>
/// Draws the tray icon at runtime instead of shipping an .ico — no binary asset to keep in sync,
/// and it uses the deck's own palette. Four keys with one lit reads as "a grid of buttons" even
/// at the 16px the tray actually renders.
/// </summary>
internal static class DeckIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create(int size = 32)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var bounds = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
            using var path = Rounded(bounds, size * 0.24f);
            using var body = new SolidBrush(Color.FromArgb(255, 23, 27, 33));
            using var edge = new Pen(Color.FromArgb(255, 70, 82, 98), Math.Max(1f, size / 22f));

            g.FillPath(body, path);
            g.DrawPath(edge, path);

            float pad = size * 0.235f;
            float gap = size * 0.10f;
            float cell = (size - pad * 2f - gap) / 2f;

            using var key = new SolidBrush(Color.FromArgb(255, 139, 149, 164));
            using var lit = new SolidBrush(Color.FromArgb(255, 63, 185, 80));

            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    var rect = new RectangleF(
                        pad + col * (cell + gap),
                        pad + row * (cell + gap),
                        cell, cell);

                    using var keyPath = Rounded(rect, cell * 0.28f);
                    g.FillPath(row == 0 && col == 0 ? lit : key, keyPath);
                }
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            // FromHandle doesn't own the handle, so clone into an icon that owns its own data
            // before the handle is destroyed.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        float diameter = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
