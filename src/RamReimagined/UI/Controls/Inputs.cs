namespace RamReimagined.UI;

/// <summary>Flat switch: filled bone when on, hairline track when off.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;
    private float HoverAmount;
    private float CheckedAmount;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Size = new Size(42, 24);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            UiAnimator.Animate(this, "checked", CheckedAmount, value ? 1f : 0f, 170,
                Value => { CheckedAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnMouseEnter(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 1f, 120, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, 0f, 160, Value => { HoverAmount = Value; Invalidate(); });
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        Checked = !Checked;
        base.OnMouseClick(e);
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
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            Checked = !Checked;
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

        var TrackWidth = Math.Min(Width, Scaled(42));
        var TrackHeight = Math.Min(Height, Scaled(24));
        var Track = new Rectangle(0, (Height - TrackHeight) / 2, TrackWidth, TrackHeight);
        var Radius = Math.Max(1, TrackHeight / 2);

        var TrackFill = Theme.Lerp(Theme.Lerp(Theme.Card, Theme.Elevated, HoverAmount), Theme.Text, CheckedAmount);
        var TrackBorder = Theme.Lerp(Theme.Lerp(Theme.Stroke, Theme.Stroke, HoverAmount), Theme.Text, CheckedAmount);
        Theme.FillRounded(g, Track, Radius, TrackFill);
        Theme.DrawRounded(g, Track, Radius, TrackBorder);

        var KnobSize = Math.Max(4, TrackHeight - Scaled(6));
        var Travel = Math.Max(0, TrackWidth - KnobSize - Scaled(6));
        var KnobX = Track.X + Scaled(3) + (int)Math.Round(Travel * CheckedAmount);
        var Knob = new Rectangle(KnobX, Track.Y + Scaled(3), KnobSize, KnobSize);
        Theme.FillRounded(g, Knob, KnobSize / 2, Theme.Lerp(Theme.Muted, Theme.Backdrop, CheckedAmount));

        if (Focused)
        {
            Theme.DrawFocus(g, new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        }
    }
}

/// <summary>Flat text input with placeholder, optional multiline body and a visible focus rule.</summary>
internal sealed class TextField : Control
{
    private readonly TextBox _box;
    private readonly Label CueLabel;
    private bool _multiline;
    private string PlaceholderText = "";
    private float HoverAmount;
    private float FocusAmount;

    public TextField(string InitialPlaceholder = "")
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.IBeam;

        // The inner TextBox must exist before any resize/layout runs, because OnLayout positions it.
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.75f),
            AutoSize = false,
            Multiline = false
        };
        _box.TextChanged += (_, _) =>
        {
            UpdateCue();
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
        _box.GotFocus += (_, _) =>
        {
            UpdateCue();
            AnimateFocus(1f);
        };
        _box.LostFocus += (_, _) =>
        {
            UpdateCue();
            AnimateFocus(0f);
        };
        _box.MouseEnter += (_, _) => AnimateHover(1f);
        _box.MouseLeave += (_, _) => AnimateHover(0f);
        Controls.Add(_box);
        _box.HandleCreated += (_, _) => Theme.ApplyDarkScrollbar(_box);

        // A native TextBox paints an opaque client area over the parent's canvas,
        // so a placeholder drawn in OnPaint is invisible. This label sits above
        // the empty box and hands focus back when clicked.
        CueLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            BackColor = Theme.Card,
            ForeColor = Theme.Faint,
            Font = Theme.Font(9.75f),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.IBeam
        };
        CueLabel.MouseDown += (_, _) => FocusInput();
        CueLabel.MouseEnter += (_, _) => AnimateHover(1f);
        CueLabel.MouseLeave += (_, _) => AnimateHover(0f);
        Controls.Add(CueLabel);
        CueLabel.BringToFront();

        Height = 38;
        Placeholder = InitialPlaceholder;
    }

    public string Placeholder
    {
        get => PlaceholderText;
        set
        {
            PlaceholderText = value ?? "";
            CueLabel.Text = PlaceholderText;
            UpdateCue();
        }
    }

    public new event EventHandler? TextChanged;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value ?? string.Empty;
    }

    public bool UseSystemPasswordChar
    {
        get => _box.UseSystemPasswordChar;
        set => _box.UseSystemPasswordChar = value;
    }

    public bool Multiline
    {
        get => _multiline;
        set
        {
            if (_multiline == value)
            {
                return;
            }

            _multiline = value;
            _box.Multiline = value;
            _box.AcceptsReturn = value;
            _box.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None;
            _box.WordWrap = value;
            CueLabel.TextAlign = value ? ContentAlignment.TopLeft : ContentAlignment.MiddleLeft;
            PerformLayout();
            UpdateCue();
            Invalidate();
        }
    }

    public void FocusInput() => _box.Focus();

    private void AnimateFocus(float Target)
    {
        UiAnimator.Animate(this, "focus", FocusAmount, Target, 150,
            Value => { FocusAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
    }

    private void AnimateHover(float Target)
    {
        UiAnimator.Animate(this, "hover", HoverAmount, Target, 140,
            Value => { HoverAmount = Value; Invalidate(); }, Theme.EaseOutCubic);
    }

    private void UpdateCue()
    {
        if (CueLabel is null)
        {
            return;
        }

        CueLabel.Visible = Enabled && !_box.Focused && string.IsNullOrEmpty(_box.Text);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_box is null || CueLabel is null)
        {
            return;
        }

        _box.BackColor = Theme.Card;
        if (_multiline)
        {
            _box.Bounds = new Rectangle(12, 10, Math.Max(10, Width - 24), Math.Max(20, Height - 20));
            CueLabel.Bounds = new Rectangle(13, 11, Math.Max(10, Width - 26), Math.Max(20, Height - 22));
        }
        else
        {
            _box.Bounds = new Rectangle(12, Math.Max(0, (Height - _box.PreferredHeight) / 2), Math.Max(10, Width - 24),
                _box.PreferredHeight);
            CueLabel.Bounds = new Rectangle(13, _box.Top, Math.Max(10, Width - 26), _box.Height);
        }

        UpdateCue();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        AnimateHover(1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            AnimateHover(0f);
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        FocusInput();
        base.OnMouseClick(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        UpdateCue();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var BoundsRect = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, BoundsRect, Theme.RadiusControl, Theme.Card);
        var BorderColor = Theme.Lerp(Theme.Lerp(Theme.StrokeSoft, Theme.Stroke, HoverAmount), Theme.Accent, FocusAmount);
        Theme.DrawRounded(g, BoundsRect, Theme.RadiusControl, BorderColor);
    }
}

/// <summary>Vertical scrolling surface that lays its children out top-to-bottom.</summary>
internal sealed class StackPanel : Panel
{
    public StackPanel()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.Transparent;
        Padding = new Padding(0, 0, 10, 0);
        HandleCreated += (_, _) => Theme.ApplyDarkScrollbar(this);
    }

    public int Gap { get; set; } = 10;

    public void Relayout()
    {
        var y = 0;
        foreach (Control child in Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            child.SetBounds(0, y, Math.Max(120, ClientSize.Width - Padding.Right), child.Height);
            y += child.Height + Gap;
        }

        AutoScrollMinSize = new Size(0, y);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Relayout();
        Invalidate();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        Relayout();
    }
}
