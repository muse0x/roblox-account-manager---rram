using System.Drawing.Drawing2D;

namespace RamReimagined.UI;

internal enum ButtonKind
{
    Primary,
    Ghost,
    Subtle,
    Danger,
    Outline
}

/// <summary>Flat, hairline button. The primary action is bone ink inverted, not a glowing accent.</summary>
internal sealed class PillButton : Control
{
    private float HoverAmount;
    private float DownAmount;
    private ButtonKind _kind = ButtonKind.Ghost;
    private bool _busy;

    public PillButton(string text = "", IconKind? icon = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Font(9.25f, FontStyle.Bold);
        ForeColor = Theme.Text;
        Text = text;
        Icon = icon;
        Height = 36;
        TabStop = true;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    public IconKind? Icon { get; set; }

    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
    }

    public int CornerRadius { get; set; } = Theme.RadiusControl;

    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            Enabled = !value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 1f, 130, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 0f, 160, Value => { HoverAmount = Value; Invalidate(); });
        UiAnimator.Animate(this, "down", DownAmount, 0f, 120, Value => { DownAmount = Value; Invalidate(); });
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        UiAnimator.Animate(this, "down", DownAmount, 1f, 70, Value => { DownAmount = Value; Invalidate(); });
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        UiAnimator.Animate(this, "down", DownAmount, 0f, 120, Value => { DownAmount = Value; Invalidate(); });
        base.OnMouseUp(e);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var Inset = (int)Math.Round(DownAmount * Theme.Px(this, 1));
        var BoundsRect = new Rectangle(Inset, Inset,
            Math.Max(1, Width - 1 - Inset * 2), Math.Max(1, Height - 1 - Inset * 2));

        var (BackgroundColor, BorderColor, ForeColor) = Palette();
        if (BackgroundColor.A > 0)
        {
            Theme.FillRounded(g, BoundsRect, CornerRadius, BackgroundColor);
        }

        if (BorderColor.A > 0)
        {
            Theme.DrawRounded(g, BoundsRect, CornerRadius, BorderColor);
        }

        var hasText = !string.IsNullOrEmpty(Text);
        if (hasText)
        {
            Theme.DrawText(g, Text, Font, ForeColor,
                new Rectangle(0, 0, Width, Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        if (Focused)
        {
            Theme.DrawFocus(g, BoundsRect, CornerRadius);
        }
    }

    private (Color Background, Color Border, Color Fore) Palette()
    {
        if (!Enabled)
        {
            return (Theme.WithAlpha(Theme.Elevated, 120), Theme.StrokeSoft, Theme.Faint);
        }

        (Color Background, Color Border, Color Fore) Normal;
        (Color Background, Color Border, Color Fore) Hot;
        (Color Background, Color Border, Color Fore) Press;

        switch (_kind)
        {
            case ButtonKind.Primary:
                Normal = (Theme.Text, Color.Empty, Theme.Backdrop);
                Hot = (Color.FromArgb(248, 246, 241), Color.Empty, Theme.Backdrop);
                Press = (Color.FromArgb(214, 210, 202), Color.Empty, Theme.Backdrop);
                break;

            case ButtonKind.Danger:
                Normal = (Color.FromArgb(20, Theme.Danger), Theme.WithAlpha(Theme.Danger, 120), Theme.Danger);
                Hot = (Color.FromArgb(32, Theme.Danger), Theme.WithAlpha(Theme.Danger, 150), Theme.Danger);
                Press = (Color.FromArgb(42, Theme.Danger), Theme.WithAlpha(Theme.Danger, 180), Theme.Danger);
                break;

            case ButtonKind.Subtle:
                Normal = (Theme.Card, Theme.StrokeSoft, Theme.Text);
                Hot = (Theme.CardHover, Theme.Stroke, Theme.Text);
                Press = (Theme.Elevated, Theme.Stroke, Theme.Text);
                break;

            case ButtonKind.Outline:
                Normal = (Color.Transparent, Theme.StrokeSoft, Theme.Text);
                Hot = (Theme.CardHover, Theme.Stroke, Theme.Text);
                Press = (Theme.Elevated, Theme.Stroke, Theme.Text);
                break;

            default:
                Normal = (Color.Transparent, Color.Empty, Theme.Muted);
                Hot = (Theme.CardHover, Color.Empty, Theme.Text);
                Press = (Theme.Elevated, Color.Empty, Theme.Text);
                break;
        }

        var BackgroundColor = Theme.Lerp(Theme.Lerp(Normal.Background, Hot.Background, HoverAmount), Press.Background, DownAmount);
        var BorderColor = Theme.Lerp(Theme.Lerp(Normal.Border, Hot.Border, HoverAmount), Press.Border, DownAmount);
        var ForeColor = Theme.Lerp(Theme.Lerp(Normal.Fore, Hot.Fore, HoverAmount), Press.Fore, DownAmount);
        return (BackgroundColor, BorderColor, ForeColor);
    }
}

internal sealed class WindowButton : Control
{
    private readonly bool _close;
    private float HoverAmount;

    public WindowButton(IconKind icon, bool close = false)
    {
        Icon = icon;
        _close = close;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(36, 30);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public IconKind Icon { get; }

    protected override void OnMouseEnter(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 1f, 130, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 0f, 160, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var Pad = Theme.Px(this, 2);
        var FillColor = _close
            ? Theme.Lerp(Color.Transparent, Color.FromArgb(196, 74, 66), HoverAmount)
            : Theme.Lerp(Color.Transparent, Theme.CardHover, HoverAmount);
        if (FillColor.A > 0)
        {
            Theme.FillRounded(g, new Rectangle(Pad, Theme.Px(this, 3), Width - Pad * 2, Height - Theme.Px(this, 6)),
                Theme.Px(this, 6), FillColor);
        }

        var Glyph = Icon switch
        {
            IconKind.Minimize => "-",
            IconKind.Maximize => "M",
            _ => "X"
        };
        var GlyphColor = Theme.Lerp(Theme.Muted, _close ? Color.FromArgb(250, 249, 246) : Theme.Text, HoverAmount);
        Theme.DrawText(g, Glyph, Theme.Font(10f, FontStyle.Bold), GlyphColor,
            new Rectangle(0, 0, Width, Height),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Left-rail navigation: flat hover, one accent rule on the active section.</summary>
internal sealed class NavRailItem : Control
{
    private bool SelectedState;
    private float HoverAmount;
    private float SelectedAmount;

    public NavRailItem(string text, IconKind icon)
    {
        Text = text;
        Icon = icon;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Font = Theme.Font(9.75f);
        Height = 38;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public IconKind Icon { get; }

    public bool Selected
    {
        get => SelectedState;
        set
        {
            if (SelectedState == value)
            {
                return;
            }

            SelectedState = value;
            UiAnimator.Animate(this, "selected", SelectedAmount, value ? 1f : 0f, 180,
                Value => { SelectedAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 1f, 130, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 0f, 170, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseLeave(e);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var Scale = DeviceDpi / 96f;
        int Scaled(int Value) => (int)Math.Round(Value * Scale);

        var BoundsRect = new Rectangle(Scaled(10), Scaled(2), Width - Scaled(20), Height - Scaled(4));

        if (SelectedAmount > 0.01f)
        {
            var SelectedFill = Theme.Lerp(Theme.WithAlpha(Theme.Card, 0), Theme.Card, SelectedAmount);
            Theme.FillRounded(g, BoundsRect, Theme.RadiusControl, SelectedFill);

            var AccentHeight = (int)Math.Round((BoundsRect.Height - Scaled(18)) * SelectedAmount);
            Theme.FillRounded(g,
                new Rectangle(0, BoundsRect.Y + (BoundsRect.Height - AccentHeight) / 2, Scaled(2), Math.Max(1, AccentHeight)),
                Math.Max(1, Scaled(1)), Theme.Accent);
        }

        if (HoverAmount > 0.01f && SelectedAmount < 0.5f)
        {
            var FillColor = Theme.Lerp(Theme.WithAlpha(Theme.CardHover, 0), Theme.WithAlpha(Theme.CardHover, 160), HoverAmount);
            Theme.FillRounded(g, BoundsRect, Theme.RadiusControl, FillColor);
        }

        var Emphasis = Math.Max(SelectedAmount, HoverAmount * 0.65f);
        var ForeColor = Theme.Lerp(Theme.Muted, Theme.Text, Emphasis);

        Theme.DrawText(g, Text, Font, ForeColor,
            new Rectangle(Scaled(24), 0, Width - Scaled(34), Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && SelectedAmount < 0.5f)
        {
            Theme.DrawFocus(g, BoundsRect, Theme.RadiusControl);
        }
    }
}
