using System.Drawing;
using System.Drawing.Drawing2D;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Generates the SmoothMice application icon at runtime — a minimalist
/// computer mouse silhouette with a curved rat tail.
/// </summary>
public static class IconFactory
{
    /// <summary>Creates the icon at the requested size (typically 16 or 32).</summary>
    public static Icon CreateMouseIcon(int size = 32)
    {
        using var bmp = DrawMouseBitmap(size);
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>Returns the raw HBITMAP handle (caller owns lifetime).</summary>
    public static IntPtr CreateMouseHBitmap(int size = 32)
    {
        using var bmp = DrawMouseBitmap(size);
        return bmp.GetHbitmap();
    }

    // ── Drawing ───────────────────────────────────────────────────────────

    private static Bitmap DrawMouseBitmap(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var s = (float)size;
        var color = Color.FromArgb(255, 60, 60, 60); // dark grey

        // ── Tail (drawn first, behind body) ──────────────────────────────
        // Bezier from bottom-left of body, curving left and down
        using var tailPen = new Pen(color, s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float bx = s * 0.28f; // body left edge approx
        float tailStartX = bx + s * 0.06f;
        float tailStartY = s * 0.82f;
        g.DrawBezier(tailPen,
            tailStartX,  tailStartY,          // start at body bottom-left
            tailStartX - s * 0.15f, s * 0.95f, // control 1 — curves down
            s * 0.08f,   s * 0.92f,            // control 2 — loops left
            s * 0.04f,   s * 0.72f);            // end — tip curves up

        // ── Mouse body (rounded rectangle) ───────────────────────────────
        float bodyL = s * 0.27f;
        float bodyT = s * 0.10f;
        float bodyW = s * 0.46f;
        float bodyH = s * 0.74f;
        float r = s * 0.12f; // corner radius

        using var bodyBrush = new SolidBrush(color);
        using var bodyPath = RoundedRect(bodyL, bodyT, bodyW, bodyH, r);
        g.FillPath(bodyBrush, bodyPath);

        // ── Button divider line (white, upper 35%) ────────────────────────
        float divY = bodyT + bodyH * 0.36f;
        using var divPen = new Pen(Color.FromArgb(220, 255, 255, 255), s * 0.045f);
        g.DrawLine(divPen, bodyL + r * 0.5f, divY, bodyL + bodyW - r * 0.5f, divY);

        // ── Centre-button gap (vertical white line in upper section) ─────
        float midX = bodyL + bodyW * 0.5f;
        using var midPen = new Pen(Color.FromArgb(200, 255, 255, 255), s * 0.04f);
        g.DrawLine(midPen, midX, bodyT + r * 0.3f, midX, divY);

        // ── Scroll wheel (small white rounded rect, centred at top) ──────
        float ww = s * 0.09f, wh = s * 0.17f;
        float wx = midX - ww / 2f;
        float wy = bodyT + bodyH * 0.08f;
        using var wheelBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        using var wheelPath = RoundedRect(wx, wy, ww, wh, s * 0.03f);
        g.FillPath(wheelBrush, wheelPath);

        return bmp;
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
