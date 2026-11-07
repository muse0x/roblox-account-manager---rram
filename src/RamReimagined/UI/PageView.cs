namespace RamReimagined.UI;

internal abstract class PageView : UserControl
{
    protected PageView(AppState app)
    {
        App = app;
        Dock = DockStyle.Fill;
        BackColor = Theme.Backdrop;
        DoubleBuffered = true;
    }

    protected AppState App { get; }

    public abstract string NavLabel { get; }

    public abstract IconKind NavIcon { get; }

    public abstract string Heading { get; }

    public abstract string Subheading { get; }

    /// <summary>Controls the shell places in the page header, right aligned.</summary>
    public virtual IReadOnlyList<Control> Actions => Array.Empty<Control>();

    public virtual void Activated()
    {
    }
}

internal sealed class RoundedCard : Panel
{
    public RoundedCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Backdrop;
        DoubleBuffered = true;
        Padding = new Padding(20, 16, 20, 16);
    }

    public int Radius { get; set; } = Theme.RadiusCard;

    public Color Fill { get; set; } = Theme.Card;

    public Color Border { get; set; } = Theme.StrokeSoft;

    public bool AccentEdge { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, bounds, Radius, Fill);
        Theme.DrawRounded(g, bounds, Radius, AccentEdge ? Theme.AccentAlpha(120) : Border);
        base.OnPaint(e);
    }
}

internal sealed class SectionTitle : Label
{
    public SectionTitle(string text)
    {
        Text = text;
        AutoSize = true;
        Font = Theme.Font(10.8f, FontStyle.Bold);
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
    }
}

internal sealed class MutedLabel : Label
{
    public MutedLabel(string text, float size = 9f)
    {
        Text = text;
        AutoSize = false;
        Font = Theme.Font(size);
        ForeColor = Theme.Muted;
        BackColor = Color.Transparent;
    }
}
