using System.Drawing.Drawing2D;

namespace RamReimagined.UI;

internal enum IconKind
{
    Play,
    Plus,
    Search,
    Refresh,
    Server,
    Users,
    Sliders,
    Info,
    Close,
    Minimize,
    Maximize,
    Copy,
    Trash,
    More,
    Grid,
    Bolt,
    External,
    Check,
    Folder,
    Sparkle,
    Globe,
    Note
}

/// <summary>Small vector glyphs so the shell needs no icon font or image assets.</summary>
internal static class Icons
{
    public static void Draw(Graphics g, IconKind kind, RectangleF box, Color color, float weight = 1.7f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(box.Width, box.Height);
        var left = box.X + (box.Width - size) / 2f;
        var top = box.Y + (box.Height - size) / 2f;
        g.TranslateTransform(left, top);
        g.ScaleTransform(size / 24f, size / 24f);

        using var pen = new Pen(color, weight * 24f / size)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case IconKind.Play:
                using (var play = new GraphicsPath())
                {
                    play.AddPolygon(new[]
                    {
                        new PointF(8f, 5.5f), new PointF(19f, 12f), new PointF(8f, 18.5f)
                    });
                    g.FillPath(brush, play);
                }
                break;

            case IconKind.Plus:
                g.DrawLine(pen, 12f, 5f, 12f, 19f);
                g.DrawLine(pen, 5f, 12f, 19f, 12f);
                break;

            case IconKind.Search:
                g.DrawEllipse(pen, 4.5f, 4.5f, 12f, 12f);
                g.DrawLine(pen, 15.5f, 15.5f, 20f, 20f);
                break;

            case IconKind.Refresh:
                g.DrawArc(pen, 4.5f, 4.5f, 15f, 15f, -40f, 280f);
                using (var arrow = new GraphicsPath())
                {
                    arrow.AddPolygon(new[]
                    {
                        new PointF(16.5f, 2.5f), new PointF(20.5f, 6.5f), new PointF(15.5f, 7.5f)
                    });
                    g.FillPath(brush, arrow);
                }
                break;

            case IconKind.Server:
                g.DrawRectangle(pen, 4f, 4.5f, 16f, 6.5f);
                g.DrawRectangle(pen, 4f, 13f, 16f, 6.5f);
                g.FillEllipse(brush, 7f, 7f, 2f, 2f);
                g.FillEllipse(brush, 7f, 15.5f, 2f, 2f);
                break;

            case IconKind.Users:
                g.DrawEllipse(pen, 8.5f, 4f, 7f, 7f);
                g.DrawArc(pen, 5.5f, 12.5f, 13f, 12f, 200f, 140f);
                break;

            case IconKind.Sliders:
                g.DrawLine(pen, 4f, 7f, 20f, 7f);
                g.DrawLine(pen, 4f, 12f, 20f, 12f);
                g.DrawLine(pen, 4f, 17f, 20f, 17f);
                g.FillEllipse(brush, 13.5f, 5f, 4f, 4f);
                g.FillEllipse(brush, 6.5f, 10f, 4f, 4f);
                g.FillEllipse(brush, 15.5f, 15f, 4f, 4f);
                break;

            case IconKind.Info:
                g.DrawEllipse(pen, 4.5f, 4.5f, 15f, 15f);
                g.FillEllipse(brush, 11f, 7.5f, 2.2f, 2.2f);
                g.DrawLine(pen, 12f, 11.5f, 12f, 16.5f);
                break;

            case IconKind.Close:
                g.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f);
                g.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f);
                break;

            case IconKind.Minimize:
                g.DrawLine(pen, 6f, 12f, 18f, 12f);
                break;

            case IconKind.Maximize:
                g.DrawRectangle(pen, 6f, 6f, 12f, 12f);
                break;

            case IconKind.Copy:
                g.DrawRectangle(pen, 4.5f, 8.5f, 10f, 10f);
                g.DrawLine(pen, 8.5f, 8.5f, 8.5f, 5.5f);
                g.DrawLine(pen, 8.5f, 5.5f, 18.5f, 5.5f);
                g.DrawLine(pen, 18.5f, 5.5f, 18.5f, 15.5f);
                g.DrawLine(pen, 18.5f, 15.5f, 15.5f, 15.5f);
                break;

            case IconKind.Trash:
                g.DrawLine(pen, 4.5f, 7f, 19.5f, 7f);
                g.DrawLine(pen, 9.5f, 7f, 9.5f, 4.5f);
                g.DrawLine(pen, 9.5f, 4.5f, 14.5f, 4.5f);
                g.DrawLine(pen, 14.5f, 4.5f, 14.5f, 7f);
                g.DrawLine(pen, 6.5f, 7f, 7.5f, 19.5f);
                g.DrawLine(pen, 17.5f, 7f, 16.5f, 19.5f);
                g.DrawLine(pen, 7.5f, 19.5f, 16.5f, 19.5f);
                break;

            case IconKind.More:
                g.FillEllipse(brush, 5f, 10.8f, 2.6f, 2.6f);
                g.FillEllipse(brush, 10.7f, 10.8f, 2.6f, 2.6f);
                g.FillEllipse(brush, 16.4f, 10.8f, 2.6f, 2.6f);
                break;

            case IconKind.Grid:
                g.DrawRectangle(pen, 4.5f, 4.5f, 6f, 6f);
                g.DrawRectangle(pen, 13.5f, 4.5f, 6f, 6f);
                g.DrawRectangle(pen, 4.5f, 13.5f, 6f, 6f);
                g.DrawRectangle(pen, 13.5f, 13.5f, 6f, 6f);
                break;

            case IconKind.Bolt:
                using (var bolt = new GraphicsPath())
                {
                    bolt.AddPolygon(new[]
                    {
                        new PointF(13.5f, 3f), new PointF(6f, 13.5f), new PointF(11.5f, 13.5f),
                        new PointF(10f, 21f), new PointF(18f, 10.5f), new PointF(12.5f, 10.5f)
                    });
                    g.FillPath(brush, bolt);
                }
                break;

            case IconKind.External:
                g.DrawLine(pen, 7f, 17f, 17.5f, 6.5f);
                g.DrawLine(pen, 11f, 5.5f, 18.5f, 5.5f);
                g.DrawLine(pen, 18.5f, 5.5f, 18.5f, 13f);
                g.DrawLine(pen, 5.5f, 12.5f, 5.5f, 18.5f);
                g.DrawLine(pen, 5.5f, 18.5f, 11.5f, 18.5f);
                break;

            case IconKind.Check:
                g.DrawLines(pen, new[] { new PointF(5.5f, 12.5f), new PointF(10f, 17f), new PointF(18.5f, 7f) });
                break;

            case IconKind.Folder:
                g.DrawLine(pen, 4f, 7f, 4f, 18.5f);
                g.DrawLine(pen, 4f, 18.5f, 20f, 18.5f);
                g.DrawLine(pen, 20f, 18.5f, 20f, 9f);
                g.DrawLine(pen, 20f, 9f, 11.5f, 9f);
                g.DrawLine(pen, 11.5f, 9f, 9.5f, 6f);
                g.DrawLine(pen, 9.5f, 6f, 4f, 6f);
                g.DrawLine(pen, 4f, 6f, 4f, 7f);
                break;

            case IconKind.Sparkle:
                g.DrawLine(pen, 12f, 3.5f, 12f, 9f);
                g.DrawLine(pen, 12f, 15f, 12f, 20.5f);
                g.DrawLine(pen, 3.5f, 12f, 9f, 12f);
                g.DrawLine(pen, 15f, 12f, 20.5f, 12f);
                g.DrawEllipse(pen, 9.5f, 9.5f, 5f, 5f);
                break;

            case IconKind.Globe:
                g.DrawEllipse(pen, 4f, 4f, 16f, 16f);
                g.DrawEllipse(pen, 8.5f, 4f, 7f, 16f);
                g.DrawLine(pen, 4f, 12f, 20f, 12f);
                break;

            case IconKind.Note:
                g.DrawRectangle(pen, 5f, 4.5f, 14f, 15f);
                g.DrawLine(pen, 8.5f, 9f, 15.5f, 9f);
                g.DrawLine(pen, 8.5f, 12.5f, 15.5f, 12.5f);
                g.DrawLine(pen, 8.5f, 16f, 13f, 16f);
                break;
        }

        g.Restore(state);
    }
}
