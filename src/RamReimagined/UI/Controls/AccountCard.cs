using System.Drawing.Imaging;

namespace RamReimagined.UI;

/// <summary>Loads avatar headshots with a disk cache and retries transient failures.</summary>
internal sealed class AvatarCache
{
    private readonly Core.RobloxApi _api;
    private readonly Dictionary<long, Image?> _cache = new();
    private readonly Dictionary<long, Task<Image?>> _pending = new();

    public AvatarCache(Core.RobloxApi api) => _api = api;

    public Image? Cached(long userId) => _cache.TryGetValue(userId, out var image) ? image : null;

    public void Forget(long userId) => _cache.Remove(userId);

    public async Task<Image?> GetAsync(long userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        if (_pending.TryGetValue(userId, out var pending))
        {
            return await pending.ConfigureAwait(false);
        }

        var task = LoadAsync(userId, ct);
        _pending[userId] = task;
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _pending.Remove(userId);
        }
    }

    private async Task<Image?> LoadAsync(long userId, CancellationToken ct)
    {
        var cachePath = Path.Combine(Core.AppPaths.AvatarCacheDirectory, $"{userId}.png");
        var diskImage = TryLoad(cachePath);
        if (diskImage is not null)
        {
            _cache[userId] = diskImage;
            return diskImage;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var url = await _api.GetAvatarHeadshotUrlAsync(userId, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(url))
                {
                    url = await _api.GetAvatarBustUrlAsync(userId, ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    var bytes = await _api.DownloadAsync(url, ct).ConfigureAwait(false);
                    var image = bytes is null ? null : Decode(bytes);
                    if (image is not null)
                    {
                        _cache[userId] = image;
                        TrySave(cachePath, bytes!);
                        return image;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Retry transient thumbnail failures below.
            }

            if (attempt < 2)
            {
                await Task.Delay(300 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static Image? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var decoded = Image.FromStream(stream);
            return (Image)decoded.Clone();
        }
        catch (Exception)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // A locked cache file is harmless.
            }

            return null;
        }
    }

    private static Image? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            return (Image)decoded.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TrySave(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception)
        {
            // A failed cache write must not prevent the avatar from rendering.
        }
    }
}

/// <summary>An account row: selection, avatar, identity, local note, presence and quick actions.</summary>
internal sealed class AccountCard : Control
{
    private readonly ToolTip _tip = new() { InitialDelay = 400, AutoPopDelay = 8000 };
    private readonly ContextMenuStrip _menu;
    private Image? _avatar;
    private Core.PresenceInfo? _presence;
    private const int RowTop = 17;
    private const int RowHeight = 30;
    private float HoverAmount;
    private bool _selected;
    private Rectangle _launchRect;
    private Rectangle _menuRect;
    private Rectangle _checkRect;

    public AccountCard(Core.Account account)
    {
        Account = account;
        Height = 64;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.Backdrop;
        Font = Theme.Font(9.5f);
        TabStop = true;

        _menu = DarkMenu.Create();
        _menu.Items.Add(DarkMenu.Item("Open in browser", () => BrowserRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Item("Open servers", () => ServersRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Item("Copy cookie", () => CopyCookieRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Item("Copy user ID", () => CopyUserIdRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Separator());
        _menu.Items.Add(DarkMenu.Item(Account.Enabled ? "Mute in batch launch" : "Include in batch launch",
            () => ToggleEnabledRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Item("Set tag", () => TagRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Item("Edit notes", () => NotesRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(DarkMenu.Separator());
        _menu.Items.Add(DarkMenu.Item("Remove account", () => RemoveRequested?.Invoke(this, EventArgs.Empty), danger: true));
    }

    public Core.Account Account { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedChanged;
    public event EventHandler? LaunchRequested;
    public event EventHandler? ServersRequested;
    public event EventHandler? BrowserRequested;
    public event EventHandler? CopyCookieRequested;
    public event EventHandler? CopyUserIdRequested;
    public event EventHandler? ToggleEnabledRequested;
    public event EventHandler? TagRequested;
    public event EventHandler? NotesRequested;
    public event EventHandler? RemoveRequested;

    public void SetAvatar(Image? image)
    {
        _avatar = image;
        Invalidate();
    }

    public void SetPresence(Core.PresenceInfo? presence)
    {
        _presence = presence;
        UpdateTooltip();
        Invalidate();
    }

    public void RefreshFromAccount()
    {
        UpdateTooltip();
        Invalidate();
    }

    private void UpdateTooltip()
    {
        var Lines = new List<string>();
        if (_presence is not null)
        {
            Lines.Add($"{_presence.State} - {_presence.Detail}");
        }

        if (!string.IsNullOrWhiteSpace(Account.Notes))
        {
            Lines.Add(Account.Notes.Trim());
        }

        if (!string.IsNullOrWhiteSpace(Account.Tag))
        {
            Lines.Add($"Tag: {Account.Tag}");
        }

        if (Account.UserId > 0)
        {
            Lines.Add($"User ID: {Account.UserId}");
        }

        if (!Account.Enabled)
        {
            Lines.Add("Muted for batches");
        }

        _tip.SetToolTip(this, string.Join(Environment.NewLine, Lines));
    }

    private void ComputeRects()
    {
        var right = Width - 14;
        _menuRect = new Rectangle(right - 28, (Height - 28) / 2, 28, 28);
        _launchRect = new Rectangle(_menuRect.Left - 10 - 88, (Height - 30) / 2, 88, 30);
        _checkRect = new Rectangle(14, (Height - 18) / 2, 18, 18);
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
        Cursor = _launchRect.Contains(e.Location) || _menuRect.Contains(e.Location) || _checkRect.Contains(e.Location)
            ? Cursors.Hand
            : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        ComputeRects();
        if (_checkRect.Contains(e.Location))
        {
            Selected = !Selected;
            Focus();
        }
        else if (_launchRect.Contains(e.Location))
        {
            LaunchRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (_menuRect.Contains(e.Location))
        {
            _menu.Show(this, new Point(Math.Max(0, _menuRect.Right - 200), _menuRect.Bottom + 6));
        }
        else
        {
            Selected = !Selected;
            Focus();
        }

        base.OnMouseClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.Handled = true;
                LaunchRequested?.Invoke(this, EventArgs.Empty);
                return;
            case Keys.Space:
                e.Handled = true;
                Selected = !Selected;
                return;
            case Keys.Apps:
                e.Handled = true;
                _menu.Show(this, new Point(_menuRect.Left, _menuRect.Bottom + 4));
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
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);

        var BackgroundColor = _selected ? Theme.Card : Theme.Lerp(Theme.Card, Theme.CardHover, HoverAmount);
        Theme.FillRounded(g, bounds, Theme.RadiusCard, BackgroundColor);
        Theme.DrawRounded(g, bounds, Theme.RadiusCard, _selected ? Theme.Accent : Theme.Lerp(Theme.StrokeSoft, Theme.Stroke, HoverAmount));

        DrawCheck(g);

        var avatarRect = new Rectangle(42, (Height - 46) / 2, 46, 46);
        DrawAvatar(g, avatarRect);

        var textLeft = 100;
        var textRight = _launchRect.Left - 14;
        var nameWidth = Math.Max(40, textRight - textLeft);

        DrawIdentityLine(g, textLeft, nameWidth);

        DrawPresence(g);
        DrawLaunch(g);
        DrawMenu(g);

        if (Focused)
        {
            Theme.DrawFocus(g, bounds, Theme.RadiusCard);
        }
    }

    private void DrawIdentityLine(Graphics g, int left, int width)
    {
        var nameFont = Theme.Font(10.8f, FontStyle.Bold);
        var rowFont = Theme.Font(10.8f);
        var name = Account.Name;
        var measured = TextRenderer.MeasureText(name, nameFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;
        var nameWidth = Math.Min(measured + 2, Math.Max(10, width));
        var handle = string.IsNullOrWhiteSpace(Account.Username) ? "" : $"@{Account.Username}";
        var handleLeft = left + nameWidth + 8;

        Theme.DrawText(g, name, nameFont, Account.Enabled ? Theme.Text : Theme.Muted,
            new Rectangle(left, RowTop, nameWidth, RowHeight),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (handleLeft < left + width)
        {
            Theme.DrawText(g, handle, rowFont, Theme.Muted,
                new Rectangle(handleLeft, RowTop, Math.Max(10, left + width - handleLeft), RowHeight),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    private void DrawCheck(Graphics g)
    {
        Theme.FillRounded(g, _checkRect, 5, _selected ? Theme.Accent : Theme.Card);
        Theme.DrawRounded(g, _checkRect, 5, _selected ? Theme.Accent : Theme.Stroke);
        if (_selected)
        {
            Theme.FillRounded(g,
                new Rectangle(_checkRect.X + 5, _checkRect.Y + 5, _checkRect.Width - 10, _checkRect.Height - 10),
                2, Theme.InkOn(Theme.Accent));
        }
    }

    private void DrawAvatar(Graphics g, Rectangle rect)
    {
        using var path = Theme.RoundedRect(rect, 8);
        if (_avatar is not null)
        {
            var state = g.Save();
            g.SetClip(path);
            g.DrawImage(_avatar, rect);
            g.Restore(state);
        }
        else
        {
            using var brush = new SolidBrush(Theme.Elevated);
            g.FillPath(brush, path);
            Theme.DrawText(g, Account.Initials, Theme.Font(13f, FontStyle.Bold), Theme.Text, rect,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private Color PresenceColor() => _presence?.Type switch
    {
        2 => Theme.Success,
        1 => Color.FromArgb(126, 158, 184),
        3 => Theme.Warning,
        _ => Color.Empty
    };

    private void DrawPresence(Graphics g)
    {
        var state = _presence;
        var text = state is null ? "Checking" : state.State;
        var color = state is null ? Theme.Faint : PresenceColor() is var c && c != Color.Empty ? c : Theme.Faint;
        var font = Theme.Font(10.8f);
        var width = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + 4;
        var rect = new Rectangle(_launchRect.Left - 12 - width, RowTop, width, RowHeight);

        Theme.DrawText(g, text, font, color, rect,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawLaunch(Graphics g)
    {
        Theme.FillRounded(g, _launchRect, Theme.RadiusControl,
            Theme.Lerp(Theme.WithAlpha(Theme.Elevated, 150), Theme.Elevated, HoverAmount));
        Theme.DrawRounded(g, _launchRect, Theme.RadiusControl,
            Theme.Lerp(Theme.StrokeSoft, Theme.Stroke, HoverAmount));
        Theme.DrawText(g, "Launch", Theme.Font(10.8f, FontStyle.Bold), Theme.Text,
            new Rectangle(_launchRect.X, RowTop, _launchRect.Width, RowHeight),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawMenu(Graphics g)
    {
        if (HoverAmount > 0.5f || _menu.Visible)
        {
            Theme.FillRounded(g, _menuRect, Theme.RadiusControl, Theme.Elevated);
        }

        Theme.DrawText(g, "...", Theme.Font(10.8f, FontStyle.Bold),
            Theme.Lerp(Theme.Muted, Theme.Text, HoverAmount),
            new Rectangle(_menuRect.X, RowTop, _menuRect.Width, RowHeight),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}