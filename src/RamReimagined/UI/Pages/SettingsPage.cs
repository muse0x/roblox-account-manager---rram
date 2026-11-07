using System.Diagnostics;
using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class SettingsPage : PageView
{
    private readonly StackPanel _stack = new() { Dock = DockStyle.Fill, Gap = 12 };
    private readonly TextField _place = new("Place ID");
    private readonly TextField _refresh = new("Seconds");
    private readonly TextField _stagger = new("Seconds");
    private readonly TextField _serverPage = new("10 - 100");
    private readonly ToggleSwitch _multi = new();
    private readonly ToggleSwitch _lowGraphics = new();
    private readonly ToggleSwitch _tile = new();
    private readonly ToggleSwitch _confirm = new();
    private readonly PillButton _save = new("Save settings", IconKind.Check) { Kind = ButtonKind.Primary, Width = 160, Height = 38 };
    private readonly List<AccentSwatch> _swatches = new();

    public SettingsPage(AppState app) : base(app)
    {
        _place.Text = app.Settings.DefaultPlaceId > 0 ? app.Settings.DefaultPlaceId.ToString() : "";
        _refresh.Text = app.Settings.RefreshSeconds.ToString();
        _stagger.Text = app.Settings.LaunchStaggerSeconds.ToString("0.#");
        _serverPage.Text = app.Settings.ServerPageSize.ToString();
        _multi.Checked = app.Settings.KeepMultiInstance;
        _lowGraphics.Checked = app.Settings.LowGraphics;
        _tile.Checked = app.Settings.TileWindows;
        _confirm.Checked = app.Settings.ConfirmBatchLaunch;

        _save.Click += (_, _) => Save();

        var accentFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Theme.Card,
            Height = 40
        };

        foreach (var (name, color) in Theme.Accents)
        {
            var swatch = new AccentSwatch(name, color)
            {
                Selected = string.Equals(name, app.Settings.AccentName, StringComparison.OrdinalIgnoreCase)
            };
            swatch.Click += (_, _) => SelectAccent(name);
            swatch.Margin = new Padding(0, 0, 10, 0);
            _swatches.Add(swatch);
            accentFlow.Controls.Add(swatch);
        }

        _stack.Controls.Add(BuildCard("Launching", new (string, Control)[]
        {
            ("Default join place ID", _place),
            ("Stagger between launches (seconds)", _stagger),
            ("Lowest graphics before launching", _lowGraphics),
            ("Tile client windows after a batch", _tile),
            ("Confirm before a batch launch", _confirm)
        }));

        _stack.Controls.Add(BuildCard("Behaviour", new (string, Control)[]
        {
            ("Keep multi-instance enabled on start", _multi),
            ("Presence refresh (seconds, min 30)", _refresh),
            ("Servers fetched per page", _serverPage)
        }));

        _stack.Controls.Add(BuildAccentCard(accentFlow));
        _stack.Controls.Add(BuildPathsCard());

        Controls.Add(_stack);
    }

    public override string NavLabel => "Settings";

    public override IconKind NavIcon => IconKind.Sliders;

    public override string Heading => "Settings";

    public override string Subheading => "Settings are stored per Windows user.";

    public override IReadOnlyList<Control> Actions => new Control[] { _save };

    private RoundedCard BuildCard(string title, IReadOnlyList<(string Label, Control Control)> rows)
    {
        var card = new RoundedCard { Height = 62 + rows.Count * 50 };
        card.Controls.Add(new SectionTitle(title) { Location = new Point(20, 18) });

        var table = new TableLayoutPanel
        {
            Location = new Point(20, 50),
            ColumnCount = 2,
            RowCount = rows.Count,
            BackColor = Theme.Card,
            Height = rows.Count * 50
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

        for (var index = 0; index < rows.Count; index++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            var label = new Label
            {
                Text = rows[index].Label,
                Font = Theme.Font(9.5f),
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };

            var control = rows[index].Control;
            if (control is TextField)
            {
                control.Size = new Size(238, 38);
            }
            else
            {
                control.Size = new Size(44, 24);
            }

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            host.Controls.Add(control);
            host.Resize += (_, _) =>
                control.Location = new Point(host.Width - control.Width, (host.Height - control.Height) / 2);

            table.Controls.Add(label, 0, index);
            table.Controls.Add(host, 1, index);
        }

        card.Controls.Add(table);
        card.Resize += (_, _) => table.Width = card.Width - 40;
        return card;
    }

    private RoundedCard BuildAccentCard(FlowLayoutPanel accentFlow)
    {
        var card = new RoundedCard { Height = 112 };
        card.Controls.Add(new SectionTitle("Accent colour") { Location = new Point(20, 18) });
        accentFlow.Location = new Point(20, 50);
        card.Controls.Add(accentFlow);
        return card;
    }

    private RoundedCard BuildPathsCard()
    {
        var card = new RoundedCard { Height = 112 };
        card.Controls.Add(new SectionTitle("Storage") { Location = new Point(20, 18) });
        var StoragePath = new MutedLabel(AppPaths.AccountsFile) { Location = new Point(20, 46), Size = new Size(620, 30), AutoEllipsis = true };
        card.Controls.Add(StoragePath);

        var open = new PillButton("Open folder", IconKind.Folder) { Kind = ButtonKind.Ghost, Width = 132, Height = 36 };
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true });
            }
            catch (Exception)
            {
                App.Toast("Could not open the data folder.", ToastKind.Warning);
            }
        };

        card.Controls.Add(open);
        card.Resize += (_, _) => open.Location = new Point(card.Width - open.Width - 20, 44);
        return card;
    }

    private void SelectAccent(string name)
    {
        App.SetAccent(name);
        foreach (var swatch in _swatches)
        {
            swatch.Selected = string.Equals(swatch.AccentName, name, StringComparison.OrdinalIgnoreCase);
        }

        if (FindForm() is { } form)
        {
            RepaintAll(form);
        }
    }

    private static void RepaintAll(Control control)
    {
        control.Invalidate(true);
        foreach (Control child in control.Controls)
        {
            RepaintAll(child);
        }
    }

    private void Save()
    {
        App.Settings.DefaultPlaceId = ParseLong(_place.Text, 0);
        App.Settings.RefreshSeconds = (int)Math.Clamp(ParseLong(_refresh.Text, 90), 30, 3600);
        App.Settings.LaunchStaggerSeconds = Math.Clamp(ParseDouble(_stagger.Text, 6), 0, 120);
        App.Settings.ServerPageSize = (int)Math.Clamp(ParseLong(_serverPage.Text, 100), 10, 100);
        App.Settings.KeepMultiInstance = _multi.Checked;
        App.Settings.LowGraphics = _lowGraphics.Checked;
        App.Settings.TileWindows = _tile.Checked;
        App.Settings.ConfirmBatchLaunch = _confirm.Checked;
        App.Settings.Save();

        if (_multi.Checked && !App.Launcher.MultiInstance.Active)
        {
            App.Launcher.MultiInstance.Enable();
        }
        else if (!_multi.Checked && App.Launcher.MultiInstance.Active)
        {
            App.Launcher.MultiInstance.Disable();
        }

        _refresh.Text = App.Settings.RefreshSeconds.ToString();
        _stagger.Text = App.Settings.LaunchStaggerSeconds.ToString("0.#");
        _serverPage.Text = App.Settings.ServerPageSize.ToString();
        App.Toast("Settings saved.", ToastKind.Success);
    }

    private static long ParseLong(string text, long fallback)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : fallback;
    }

    private static double ParseDouble(string text, double fallback)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}

internal sealed class AccentSwatch : Control
{
    private bool _hover;

    public AccentSwatch(string accentName, Color color)
    {
        AccentName = accentName;
        AccentColor = color;
        Size = new Size(34, 34);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public string AccentName { get; }

    public Color AccentColor { get; }

    public bool Selected { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        if (Selected)
        {
            Theme.DrawRounded(g, outer, 17, Theme.Accent, 1.6f);
        }
        else if (_hover)
        {
            Theme.DrawRounded(g, outer, 17, Theme.Stroke, 1.2f);
        }

        var dot = new Rectangle(6, 6, Width - 13, Height - 13);
        Theme.FillRounded(g, dot, 14, AccentColor);
    }
}
