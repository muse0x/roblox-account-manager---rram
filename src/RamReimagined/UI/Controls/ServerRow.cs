using RamReimagined.Core;

namespace RamReimagined.UI;

/// <summary>One row in the server browser: job id, population, network stats and a join action.</summary>
internal sealed class ServerRow : Control
{
    private float HoverAmount;
    private Rectangle _joinRect;

    public ServerRow(PublicServer server)
    {
        Server = server;
        Height = 54;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.Backdrop;
        TabStop = true;
    }

    public PublicServer Server { get; }

    public event EventHandler? JoinRequested;

    private void ComputeRects()
    {
        _joinRect = new Rectangle(Width - 16 - 76, (Height - 30) / 2, 76, 30);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ComputeRects();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 1f, 130,
            Value => { HoverAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 0f, 170,
            Value => { HoverAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ComputeRects();
        Cursor = _joinRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        ComputeRects();
        Focus();
        if (_joinRect.Contains(e.Location))
        {
            JoinRequested?.Invoke(this, EventArgs.Empty);
        }

        base.OnMouseClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            JoinRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        ComputeRects();

        if (HoverAmount > 0.01f)
        {
            Theme.FillRounded(g, new Rectangle(0, 1, Width - 1, Height - 3), Theme.RadiusControl,
                Theme.Lerp(Theme.WithAlpha(Theme.Card, 0), Theme.Card, HoverAmount));
        }

        Theme.DrawHairline(g, 16, Height - 2, Width - 32);

        Theme.DrawText(g, Server.Id, Theme.Mono(10f), Theme.Text, new Rectangle(16, 0, 206, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var barBounds = new Rectangle(236, (Height - 6) / 2 + 8, 142, 4);
        Theme.FillRounded(g, barBounds, 2, Theme.Stroke);
        var fill = Math.Max(2, (int)(barBounds.Width * Server.Fill));
        Theme.FillRounded(g, new Rectangle(barBounds.X, barBounds.Y, fill, barBounds.Height), 2,
            Server.Fill > 0.85 ? Theme.Warning : Theme.Muted);
        Theme.DrawText(g, $"{Server.Playing}/{Server.MaxPlayers}", Theme.Mono(10f), Theme.Muted,
            new Rectangle(236, 8, 150, 20));

        var pingColor = Server.Ping <= 0 ? Theme.Faint : Server.Ping < 80 ? Theme.Success : Server.Ping < 160 ? Theme.Warning : Theme.Danger;
        Theme.DrawText(g, Server.Ping <= 0 ? "n/a" : $"{Server.Ping} ms", Theme.Mono(10f), pingColor,
            new Rectangle(398, 0, 78, Height));
        Theme.DrawText(g, Server.Fps <= 0 ? "n/a" : $"{Server.Fps:0} fps", Theme.Mono(10f), Theme.Muted,
            new Rectangle(478, 0, 78, Height));

        Theme.FillRounded(g, _joinRect, Theme.RadiusControl, Theme.Lerp(Theme.Card, Theme.Elevated, HoverAmount));
        Theme.DrawRounded(g, _joinRect, Theme.RadiusControl, Theme.Lerp(Theme.StrokeSoft, Theme.Stroke, HoverAmount));
        Theme.DrawText(g, "Join", Theme.Font(9f, FontStyle.Bold), Theme.Text, _joinRect,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (Focused)
        {
            Theme.DrawFocus(g, new Rectangle(0, 1, Width - 1, Height - 3), Theme.RadiusControl);
        }
    }
}

/// <summary>Column captions that track the server row layout.</summary>
internal sealed class ServerTableHeader : Control
{
    public ServerTableHeader()
    {
        Height = 30;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Backdrop;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var font = Theme.Mono(9f);
        Theme.DrawText(g, "JOB ID", font, Theme.Faint, new Rectangle(16, 0, 200, Height));
        Theme.DrawText(g, "PLAYERS", font, Theme.Faint, new Rectangle(236, 0, 150, Height));
        Theme.DrawText(g, "PING", font, Theme.Faint, new Rectangle(398, 0, 70, Height));
        Theme.DrawText(g, "FPS", font, Theme.Faint, new Rectangle(478, 0, 70, Height));
        Theme.DrawHairline(g, 16, Height - 2, 900);
    }
}
