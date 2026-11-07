using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class MainForm : Form
{
    private const int ResizeBorder = 6;

    private readonly AppState _app;
    private readonly RailPanel _rail;
    private readonly Panel _header;
    private readonly Label _heading;
    private readonly Label _subheading;
    private readonly FlowLayoutPanel _actions;
    private readonly Panel _pageHost;
    private readonly StatusToast _toast;
    private readonly WindowButton MinimizeButton;
    private readonly WindowButton MaximizeButton;
    private readonly WindowButton CloseButton;
    private readonly System.Windows.Forms.Timer _presenceTimer;
    private readonly List<(PageView Page, NavRailItem Item)> _pages = new();
    private PageView? _current;
    private int PageOffset;
    private bool _refreshingPresence;

    public MainForm()
    {
        _app = new AppState();

        AutoScaleMode = AutoScaleMode.None;
        Text = "RRAM — Reimagine Roblox Account Manager";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Backdrop;
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception)
        {
            // The exe icon is cosmetic; a missing shell association must not stop startup.
        }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        ClientSize = new Size(Math.Clamp(_app.Settings.WindowWidth, 1180, 2400),
            Math.Clamp(_app.Settings.WindowHeight, 720, 1500));
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        _rail = new RailPanel { Dock = DockStyle.Fill };

        _header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop };

        _heading = new Label
        {
            AutoSize = false,
            Font = Theme.FontDisplay(19f),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 32
        };
        _subheading = new Label
        {
            AutoSize = false,
            Font = Theme.Font(9.5f),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Height = 22
        };
        _actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.Transparent,
            Height = 38,
            Margin = Padding.Empty
        };

        var MinimizeControl = new WindowButton(IconKind.Minimize);
        MinimizeControl.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var MaximizeControl = new WindowButton(IconKind.Maximize);
        MaximizeControl.Click += (_, _) => ToggleMaximize();
        var CloseControl = new WindowButton(IconKind.Close, close: true);
        CloseControl.Click += (_, _) => Close();
        MinimizeButton = MinimizeControl;
        MaximizeButton = MaximizeControl;
        CloseButton = CloseControl;

        _header.Controls.Add(_heading);
        _header.Controls.Add(_subheading);
        _header.Controls.Add(_actions);
        _header.Controls.Add(MinimizeControl);
        _header.Controls.Add(MaximizeControl);
        _header.Controls.Add(CloseControl);

        _header.Resize += (_, _) => LayoutHeader();

        _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop, Padding = new Padding(30, 0, 30, 22) };
        _pageHost.Resize += (_, _) => LayoutPage(PageOffset);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Backdrop,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(_header, 0, 0);
        content.Controls.Add(_pageHost, 0, 1);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Backdrop,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(_rail, 0, 0);
        root.Controls.Add(content, 1, 0);

        Controls.Add(root);

        _toast = new StatusToast();
        Controls.Add(_toast);
        _toast.BringToFront();

        _app.ToastRequested += OnToast;

        BuildPages();

        foreach (var draggable in new Control[] { _header, _heading, _subheading })
        {
            draggable.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    WindowChrome.Drag(this);
                }
            };
            draggable.DoubleClick += (_, _) => ToggleMaximize();
        }

        _presenceTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _presenceTimer.Tick += async (_, _) => await RefreshPresenceAsync();
        _presenceTimer.Start();

        Load += (_, _) =>
        {            UpdateRegion();
            PositionToast();
            ShowPage(_pages[0].Page);
            _ = RefreshPresenceAsync();
            AppTimers.SetInterval(_presenceTimer, Math.Max(30, _app.Settings.RefreshSeconds) * 1000);
        };

        Resize += (_, _) =>
        {
            UpdateRegion();
            PositionToast();
        };

        FormClosing += (_, _) =>
        {
            _app.Settings.WindowWidth = WindowState == FormWindowState.Normal ? Width : _app.Settings.WindowWidth;
            _app.Settings.WindowHeight = WindowState == FormWindowState.Normal ? Height : _app.Settings.WindowHeight;
            _app.Settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            _app.Settings.Save();
        };

        FormClosed += (_, _) =>
        {
            _presenceTimer.Stop();
            _presenceTimer.Dispose();
            _app.Dispose();
        };

        if (_app.Settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void BuildPages()
    {
        var accounts = new AccountsPage(_app);
        var servers = new ServersPage(_app);
        var versions = new VersionsPage(_app);
        var tools = new ToolsPage(_app);
        var settings = new SettingsPage(_app);

        accounts.OpenServersRequested += (_, _) => ShowPage(servers);

        AddPage(accounts);
        AddPage(servers);
        AddPage(versions);
        AddPage(tools);
        AddPage(settings);
    }

    private void AddPage(PageView TargetPage)
    {
        var RailItem = new NavRailItem(TargetPage.NavLabel, TargetPage.NavIcon);
        RailItem.Click += (_, _) => ShowPage(TargetPage);
        _rail.AddItem(RailItem);
        _pages.Add((TargetPage, RailItem));
    }

    private void ShowPage(PageView TargetPage)
    {
        if (ReferenceEquals(_current, TargetPage))
        {
            return;
        }

        _current = TargetPage;
        _pageHost.SuspendLayout();
        _pageHost.Controls.Clear();
        TargetPage.Dock = DockStyle.None;
        _pageHost.Controls.Add(TargetPage);
        _pageHost.ResumeLayout();

        PageOffset = Theme.Px(_pageHost, 10);
        LayoutPage(PageOffset);
        UiAnimator.Animate(this, "TargetPage", PageOffset, 0f, 220,
            Value => { PageOffset = (int)Math.Round(Value); LayoutPage(PageOffset); }, Theme.EaseOutCubic);

        _heading.Text = TargetPage.Heading;
        _subheading.Text = TargetPage.Subheading;
        _heading.ForeColor = Theme.Muted;
        _subheading.ForeColor = Theme.WithAlpha(Theme.Muted, 170);
        UiAnimator.Animate(this, "heading", 0f, 1f, 200, Value =>
        {
            _heading.ForeColor = Theme.Lerp(Theme.Muted, Theme.Text, Value);
            _subheading.ForeColor = Theme.Lerp(Theme.WithAlpha(Theme.Muted, 170), Theme.Muted, Value);
        }, Theme.EaseOutCubic);

        _actions.SuspendLayout();
        _actions.Controls.Clear();
        foreach (var ActionControl in TargetPage.Actions)
        {
            ActionControl.Margin = new Padding(8, 0, 0, 0);
            _actions.Controls.Add(ActionControl);
        }

        _actions.ResumeLayout();
        _actions.PerformLayout();
        LayoutHeader();

        foreach (var (Candidate, RailItem) in _pages)
        {
            var IsTarget = ReferenceEquals(Candidate, TargetPage);
            RailItem.Selected = IsTarget;
            if (IsTarget)
            {
                RailItem.Focus();
            }
        }

        AppTimers.SetInterval(_presenceTimer, Math.Max(30, _app.Settings.RefreshSeconds) * 1000);
        TargetPage.Activated();
    }

    private void LayoutPage(int Offset)
    {
        if (_current is null)
        {
            return;
        }

        var DisplayRect = _pageHost.DisplayRectangle;
        _current.SetBounds(DisplayRect.Left, DisplayRect.Top + Offset, Math.Max(1, DisplayRect.Width), Math.Max(1, DisplayRect.Height));
    }

    private void LayoutHeader()
    {
        if (_header.ClientSize.Width <= 0)
        {
            return;
        }

        var TopOffset = Theme.Px(_header, 10);
        CloseButton.Location = new Point(_header.ClientSize.Width - Theme.Px(_header, 14) - CloseButton.Width, TopOffset);
        MaximizeButton.Location = new Point(CloseButton.Left - MaximizeButton.Width, TopOffset);
        MinimizeButton.Location = new Point(MaximizeButton.Left - MinimizeButton.Width, TopOffset);

        var ActionWidth = 0;
        foreach (Control ActionControl in _actions.Controls)
        {
            ActionWidth += ActionControl.Width + ActionControl.Margin.Horizontal;
        }

        ActionWidth = Math.Max(ActionWidth, _actions.PreferredSize.Width);
        var ActionRight = MinimizeButton.Left - Theme.Px(_header, 16);
        _actions.Location = new Point(Math.Max(Theme.Px(_header, 30), ActionRight - ActionWidth), Theme.Px(_header, 22));

        var TextLeft = Theme.Px(_header, 30);
        var TextRight = _actions.Left - Theme.Px(_header, 14);
        var TextWidth = Math.Max(Theme.Px(_header, 160), TextRight - TextLeft);
        var HeadingTop = Theme.Px(_header, 12);
        var HeadingHeight = _heading.Font.Height + Theme.Px(_header, 4);
        _heading.SetBounds(TextLeft, HeadingTop, TextWidth, HeadingHeight);
        _subheading.SetBounds(TextLeft, _heading.Bottom + Theme.Px(_header, 1),
            TextWidth, _subheading.Font.Height + Theme.Px(_header, 4));
    }

    private void OnToast(string message, ToastKind kind)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnToast(message, kind)));
            return;
        }

        _toast.ShowMessage(message, kind);
        PositionToast();
    }

    private void PositionToast()
    {
        _toast.Location = new Point(
            Math.Max(12, ClientSize.Width - _toast.Width - 30),
            Math.Max(12, ClientSize.Height - _toast.Height - 26));
    }

    private async Task RefreshPresenceAsync()
    {
        if (_refreshingPresence)
        {
            return;
        }

        _refreshingPresence = true;
        try
        {
            await _app.RefreshPresenceAsync();
        }
        catch (Exception)
        {
            // Presence is decorative; a failure must not interrupt the UI.
        }
        finally
        {
            _refreshingPresence = false;
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    private void UpdateRegion()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            Region = null;
            return;
        }

        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Theme.RadiusWindow);
        Region = new Region(path);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var Selected = _pages.FirstOrDefault(Entry => ReferenceEquals(Entry.Page, _current));
        Selected.Item?.Focus();
    }

    protected override void WndProc(ref Message m)
    {
        const int wmNcHitTest = 0x84;

        if (m.Msg == wmNcHitTest && WindowState == FormWindowState.Normal)
        {
            var raw = m.LParam.ToInt64();
            var screenPoint = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
            var point = PointToClient(screenPoint);

            var left = point.X <= ResizeBorder;
            var right = point.X >= ClientSize.Width - ResizeBorder;
            var top = point.Y <= ResizeBorder;
            var bottom = point.Y >= ClientSize.Height - ResizeBorder;

            if (left && top) { m.Result = (IntPtr)13; return; }
            if (right && top) { m.Result = (IntPtr)14; return; }
            if (left && bottom) { m.Result = (IntPtr)16; return; }
            if (right && bottom) { m.Result = (IntPtr)17; return; }
            if (left) { m.Result = (IntPtr)10; return; }
            if (right) { m.Result = (IntPtr)11; return; }
            if (top) { m.Result = (IntPtr)12; return; }
            if (bottom) { m.Result = (IntPtr)15; return; }
        }

        base.WndProc(ref m);
    }
}

internal static class AppTimers
{
    public static void SetInterval(System.Windows.Forms.Timer timer, int milliseconds)
    {
        var interval = Math.Clamp(milliseconds, 5_000, 3_600_000);
        if (timer.Interval == interval)
        {
            return;
        }

        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }
}

/// <summary>Left navigation rail: RRAM wordmark, section items and an animated selection pill.</summary>
internal sealed class RailPanel : Panel
{
    private readonly List<NavRailItem> Items = new();
    private readonly Image? Logo = LoadLogo();

    public RailPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Rail;
    }

    private static Image? LoadLogo()
    {
        try
        {
            var LogoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "RRAM-logo.png");
            if (!System.IO.File.Exists(LogoPath))
            {
                return null;
            }

            using var Stream = System.IO.File.OpenRead(LogoPath);
            using var Decoded = Image.FromStream(Stream);
            return (Image)Decoded.Clone();
        }
        catch (Exception)
        {
            // A missing logo falls back to a text mark.
            return null;
        }
    }

    public void AddItem(NavRailItem RailItem)
    {
        Items.Add(RailItem);
        Controls.Add(RailItem);
        LayoutItems();
    }

    private void LayoutItems()
    {
        var Scale = DeviceDpi / 96f;
        int Scaled(int Value) => (int)Math.Round(Value * Scale);

        var Y = Scaled(138);
        var ItemHeight = Scaled(42);
        var Gap = Scaled(2);
        foreach (var Item in Items)
        {
            Item.SetBounds(0, Y, Width, ItemHeight);
            Y += ItemHeight + Gap;
        }
    }

    protected override void OnResize(EventArgs EventArguments)
    {
        base.OnResize(EventArguments);
        LayoutItems();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var G = e.Graphics;
        Theme.EnableQuality(G);
        var Scale = DeviceDpi / 96f;
        int Scaled(int Value) => (int)Math.Round(Value * Scale);

        using (var Brush = new SolidBrush(Theme.Rail))
        {
            G.FillRectangle(Brush, ClientRectangle);
        }

        var LogoWidth = Scaled(146);
        var LogoHeight = Logo is null
            ? Scaled(62)
            : Math.Max(1, (int)Math.Round(Logo.Height * (LogoWidth / (double)Logo.Width)));
        var LogoRect = new Rectangle(Scaled(24), Scaled(14), LogoWidth, LogoHeight);
        if (Logo is not null)
        {
            G.DrawImage(Logo, LogoRect);
        }
        else
        {
            Theme.DrawText(G, "RRAM", Theme.Font(19f, FontStyle.Bold), Theme.Text, LogoRect,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        var SubFont = Theme.Mono(7f);
        var LineHeight = SubFont.Height + Scaled(2);
        var SubY = LogoRect.Bottom + Scaled(5);
        Theme.DrawText(G, "REIMAGINE ROBLOX", SubFont, Theme.Faint,
            new Rectangle(Scaled(24), SubY, Width - Scaled(48), SubFont.Height),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        Theme.DrawText(G, "ACCOUNT MANAGER", SubFont, Theme.Faint,
            new Rectangle(Scaled(24), SubY + LineHeight, Width - Scaled(48), SubFont.Height),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        var HairY = SubY + LineHeight + SubFont.Height + Scaled(9);
        Theme.DrawHairline(G, Scaled(24), HairY, Width - Scaled(48));

        using var Pen = new Pen(Theme.StrokeSoft);
        G.DrawLine(Pen, Width - 1, 0, Width - 1, Height);
    }

    protected override void Dispose(bool Disposing)
    {
        if (Disposing)
        {
            Logo?.Dispose();
        }

        base.Dispose(Disposing);
    }
}