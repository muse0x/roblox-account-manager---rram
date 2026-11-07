using System.Runtime.InteropServices;

namespace RamReimagined.UI;

internal static class WindowChrome
{
    public const int WmNcLButtonDown = 0xA1;
    public const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Drag(Form form)
    {
        if (form.WindowState == FormWindowState.Maximized)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(form.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }
}

internal enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Dark context menu shared by the account cards and the toolbar.</summary>
internal static class DarkMenu
{
    public static ContextMenuStrip Create()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            ShowImageMargin = false,
            Font = Theme.Font(9.25f),
            Padding = new Padding(0, 4, 0, 4)
        };
        menu.Renderer = new Renderer();
        return menu;
    }

    public static ToolStripMenuItem Item(string text, Action action, bool danger = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = danger ? Theme.Danger : Theme.Text,
            Padding = new Padding(12, 6, 16, 6)
        };
        item.Click += (_, _) => action();
        return item;
    }

    public static ToolStripSeparator Separator()
    {
        var separator = new ToolStripSeparator();
        return separator;
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.ForeColor ?? Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Theme.Stroke);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Panel;
            public override Color ImageMarginGradientBegin => Theme.Panel;
            public override Color ImageMarginGradientMiddle => Theme.Panel;
            public override Color ImageMarginGradientEnd => Theme.Panel;
            public override Color MenuItemSelected => Theme.CardHover;
            public override Color MenuItemBorder => Theme.Stroke;
            public override Color SeparatorDark => Theme.StrokeSoft;
            public override Color SeparatorLight => Theme.Panel;
        }
    }
}

/// <summary>Floating status strip that confirms every action in the shell.</summary>
internal sealed class StatusToast : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4200 };
    private string _message = "";
    private Color _accent = Theme.Accent;

    public StatusToast()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.Font(9.25f);
        Width = 360;
        Height = 46;
        Visible = false;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Visible = false;
        };
    }

    public void ShowMessage(string message, ToastKind kind)
    {
        _message = message;
        _accent = kind switch
        {
            ToastKind.Success => Theme.Success,
            ToastKind.Warning => Theme.Warning,
            ToastKind.Error => Theme.Danger,
            _ => Theme.Muted
        };

        var width = Math.Min(560, TextRenderer.MeasureText(message, Font).Width + 76);
        Width = Math.Max(260, width);
        Visible = true;
        BringToFront();
        Invalidate();
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.EnableQuality(g);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, bounds, Theme.RadiusControl, Theme.Panel);
        Theme.DrawRounded(g, bounds, Theme.RadiusControl, Theme.Stroke);

        Theme.FillRounded(g, new Rectangle(12, (Height - 8) / 2, 3, 8), 2, _accent);

        Theme.DrawText(g, _message, Font, Theme.Text,
            new Rectangle(26, 0, Width - 38, Height),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Dark modal sheet used for prompts and confirmations.</summary>
internal sealed class SheetForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _messageLabel;
    private readonly Panel _bodyHost;
    private readonly PillButton _ok;
    private readonly PillButton _cancel;

    public SheetForm(string title, string message, string okText, int bodyHeight, bool danger)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = Theme.Panel;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(500, 206 + bodyHeight);
        Padding = new Padding(1);

        _titleLabel = new Label
        {
            Text = title,
            Font = Theme.Font(12.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(24, 20, 400, 30)
        };

        _messageLabel = new Label
        {
            Text = message,
            Font = Theme.Font(9.25f),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            AutoSize = false,
            Bounds = new Rectangle(24, 50, 452, 46)
        };

        _bodyHost = new Panel
        {
            BackColor = Theme.Panel,
            Bounds = new Rectangle(24, 100, 452, bodyHeight)
        };

        _ok = new PillButton(okText) { Kind = danger ? ButtonKind.Danger : ButtonKind.Primary, Width = 132, Height = 38 };
        _ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _cancel = new PillButton("Cancel") { Kind = ButtonKind.Ghost, Width = 96, Height = 38 };
        _cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var close = new WindowButton(IconKind.Close, close: true) { Bounds = new Rectangle(ClientSize.Width - 54, 10, 36, 30) };
        close.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        _ok.Location = new Point(ClientSize.Width - 24 - _ok.Width, ClientSize.Height - 58);
        _cancel.Location = new Point(_ok.Left - 8 - _cancel.Width, _ok.Top);

        Controls.Add(_titleLabel);
        Controls.Add(_messageLabel);
        Controls.Add(_bodyHost);
        Controls.Add(_cancel);
        Controls.Add(_ok);
        Controls.Add(close);

        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                WindowChrome.Drag(this);
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter && ActiveControl is not TextBox { Multiline: true })
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
    }

    public void SetBody(Control control, int height)
    {
        control.SetBounds(0, 0, _bodyHost.Width, height);
        _bodyHost.Controls.Add(control);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.EnableQuality(e.Graphics);
        Theme.DrawRounded(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.RadiusWindow, Theme.Stroke);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), Theme.RadiusWindow);
        Region = new Region(path);
    }
}

internal static class Dialogs
{
    public static string? Ask(IWin32Window? owner, string title, string message, string placeholder = "",
        bool secret = false, string okText = "Continue", int bodyHeight = 42)
    {
        using var form = new SheetForm(title, message, okText, bodyHeight, danger: false);
        var field = new TextField(placeholder) { UseSystemPasswordChar = secret, Height = bodyHeight };
        form.SetBody(field, bodyHeight);
        field.FocusInput();
        return form.ShowDialog(owner) == DialogResult.OK ? field.Text.Trim() : null;
    }

    public static string? AskMultiline(IWin32Window? owner, string title, string message, string placeholder = "",
        string okText = "Save", int bodyHeight = 120)
    {
        using var form = new SheetForm(title, message, okText, bodyHeight + 8, danger: false);
        var field = new TextField(placeholder) { Multiline = true, Height = bodyHeight, Text = placeholder };
        field.Text = "";
        form.SetBody(field, bodyHeight);
        field.FocusInput();
        return form.ShowDialog(owner) == DialogResult.OK ? field.Text.Trim() : null;
    }

    public static bool Confirm(IWin32Window? owner, string title, string message, string okText = "Confirm", bool danger = true)
    {
        using var form = new SheetForm(title, message, okText, 0, danger);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}
