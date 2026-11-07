using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace RamReimagined.UI;

/// <summary>
/// Design tokens taken from the vault's operative rules: one near-black ground,
/// one bone ink, one accent, no decorative gradients, hairline separation, and a
/// grotesque for words with a mono reserved for measured values.
/// </summary>
internal static class Theme
{
    public static Color Backdrop { get; } = Color.FromArgb(8, 8, 10);
    public static Color Rail { get; } = Color.FromArgb(11, 11, 14);
    public static Color Panel { get; } = Color.FromArgb(15, 15, 18);
    public static Color Card { get; } = Color.FromArgb(17, 17, 21);
    public static Color CardHover { get; } = Color.FromArgb(24, 24, 29);
    public static Color Elevated { get; } = Color.FromArgb(29, 29, 35);
    public static Color Stroke { get; } = Color.FromArgb(43, 43, 50);
    public static Color StrokeSoft { get; } = Color.FromArgb(30, 30, 36);

    // Bone ink from the vault's best-rated reference, not pure white.
    public static Color Text { get; } = Color.FromArgb(233, 230, 224);
    public static Color Muted { get; } = Color.FromArgb(168, 164, 156);
    public static Color Faint { get; } = Color.FromArgb(138, 134, 126);

    // Status colours are information, not decoration.
    public static Color Success { get; } = Color.FromArgb(111, 176, 123);
    public static Color Warning { get; } = Color.FromArgb(214, 162, 75);
    public static Color Danger { get; } = Color.FromArgb(210, 104, 92);

    public const int RadiusWindow = 12;
    public const int RadiusCard = 10;
    public const int RadiusControl = 8;

    public static Color Accent { get; private set; } = Color.FromArgb(194, 121, 91);

    /// <summary>One accent at a time. The default is a warm clay, not the generated violet-blue.</summary>
    public static readonly (string Name, Color Color)[] Accents =
    {
        ("Ember", Color.FromArgb(194, 121, 91)),
        ("Sage", Color.FromArgb(126, 154, 120)),
        ("Tide", Color.FromArgb(94, 140, 150)),
        ("Plum", Color.FromArgb(144, 121, 166)),
        ("Bone", Color.FromArgb(201, 197, 188))
    };

    public static void SetAccent(string? name)
    {
        var match = Accents.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        Accent = match.Color == default ? Accents[0].Color : match.Color;
    }

    public static Color AccentAlpha(int alpha) => Color.FromArgb(alpha, Accent);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr Hwnd, string? SubAppName, string? SubIdList);

    /// <summary>Applies the dark Explorer theme to native scrollbars.</summary>
    public static void ApplyDarkScrollbar(Control ControlInstance)
    {
        if (!ControlInstance.IsHandleCreated)
        {
            return;
        }

        try
        {
            SetWindowTheme(ControlInstance.Handle, "DarkMode_Explorer", null);
        }
        catch (Exception)
        {
            // Older Windows builds may not expose the dark scrollbar theme.
        }
    }

    public static Color Lerp(Color FromColor, Color ToColor, float Amount)
    {
        Amount = Math.Clamp(Amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(FromColor.A + (ToColor.A - FromColor.A) * Amount),
            (int)Math.Round(FromColor.R + (ToColor.R - FromColor.R) * Amount),
            (int)Math.Round(FromColor.G + (ToColor.G - FromColor.G) * Amount),
            (int)Math.Round(FromColor.B + (ToColor.B - FromColor.B) * Amount));
    }

    public static float EaseOutCubic(float Progress)
    {
        Progress = Math.Clamp(Progress, 0f, 1f);
        var Inverse = 1f - Progress;
        return 1f - Inverse * Inverse * Inverse;
    }

    public static float EaseInOutCubic(float Progress)
    {
        Progress = Math.Clamp(Progress, 0f, 1f);
        return Progress < 0.5f ? 4f * Progress * Progress * Progress : 1f - MathF.Pow(-2f * Progress + 2f, 3f) / 2f;
    }

    /// <summary>Scales a design-space measurement to the control's current DPI.</summary>
    public static int Px(Control ControlInstance, int Logical)
    {
        return (int)Math.Round(Logical * ControlInstance.DeviceDpi / 96f);
    }

    /// <summary>Ink that stays readable on a given fill (used by swatches and the primary button).</summary>
    public static Color InkOn(Color color)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance > 0.6 ? Backdrop : Color.FromArgb(250, 249, 246);
    }

    private static readonly Lazy<string[]> InstalledFamilies = new(() =>
    {
        try
        {
            return FontFamily.Families.Select(family => family.Name).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    });

    private static string Pick(params string[] names)
    {
        var installed = InstalledFamilies.Value;
        foreach (var name in names)
        {
            if (installed.Any(family => family.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return names[^1];
    }

    public static readonly string UiFamily = Pick("Segoe UI Variable Text", "Segoe UI", "Arial");
    public static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", "Arial");
    public static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

    private static readonly Dictionary<(int Family, float Size, FontStyle Style), Font> FontCache = new();

    /// <summary>
    /// Existing call sites ask for 7.5 to 19 pt. Functional text under 11 px was the
    /// worst offender in the first build, so every request is mapped onto one
    /// legible scale instead of being trusted as written.
    /// </summary>
    private static float Legible(float size) => size switch
    {
        <= 8.9f => 11f,
        <= 9.5f => 11.5f,
        <= 10.2f => 12f,
        <= 11f => 12.5f,
        <= 12.2f => 13f,
        <= 13.5f => 14f,
        <= 15f => 15.5f,
        <= 16.5f => 17f,
        <= 18f => 19f,
        _ => size
    };

    public static Font Font(float size, FontStyle style = FontStyle.Regular) =>
        GetFont(0, UiFamily, Legible(size), style);

    public static Font FontDisplay(float size, FontStyle style = FontStyle.Bold) =>
        GetFont(1, DisplayFamily, Legible(size), style);

    /// <summary>Mono is only for measured values: user IDs, job IDs, ping, FPS, counts, versions.</summary>
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        GetFont(2, MonoFamily, Math.Max(10f, size), style);

    private static Font GetFont(int familyIndex, string family, float size, FontStyle style)
    {
        var key = (familyIndex, size, style);
        if (!FontCache.TryGetValue(key, out var font))
        {
            font = new Font(family, size, style, GraphicsUnit.Point);
            FontCache[key] = font;
        }

        return font;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        if (r <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = r * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Rectangle bounds, int radius, Color color, float width = 1f)
    {
        using var path = RoundedRect(new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1), radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Kept for compatibility; new surfaces separate by tone and hairline instead.</summary>
    public static void FillGradient(Graphics g, Rectangle bounds, int radius, Color from, Color to, float angle = 35f)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new LinearGradientBrush(bounds, from, to, angle);
        g.FillPath(brush, path);
    }

    public static void DrawHairline(Graphics g, int x, int y, int width)
    {
        using var pen = new Pen(StrokeSoft);
        g.DrawLine(pen, x, y, x + width, y);
    }

    /// <summary>Visible keyboard focus, drawn by every custom control.</summary>
    public static void DrawFocus(Graphics g, Rectangle bounds, int radius)
    {
        if (bounds.Width < 10 || bounds.Height < 10)
        {
            return;
        }

        using var path = RoundedRect(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 5, bounds.Height - 5),
            Math.Max(3, radius - 2));
        using var pen = new Pen(Accent, 1.6f) { DashStyle = DashStyle.Solid };
        g.DrawPath(pen, path);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis)
    {
        TextRenderer.DrawText(g, text, font, bounds, color, flags);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Point location)
    {
        TextRenderer.DrawText(g, text, font, location, color, TextFormatFlags.Left | TextFormatFlags.Top);
    }

    public static Color WithAlpha(Color color, int alpha) => Color.FromArgb(alpha, color);

    public static void EnableQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }
}
