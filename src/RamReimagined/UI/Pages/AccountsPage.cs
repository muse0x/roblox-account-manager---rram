using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class AccountsPage : PageView
{
    private readonly TextField _search;
    private readonly TextField _place;
    private readonly PillButton _recent;
    private readonly PillButton _addBrowser;
    private readonly PillButton _addCookie;
    private readonly PillButton _refresh;
    private readonly PillButton _launch;
    private readonly PillButton _more;
    private readonly Panel _list;
    private readonly Label _count;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 200 };
    private readonly Dictionary<Guid, AccountCard> _cards = new();
    private bool _busy;
    private float EmptyProgress = 1f;

    public AccountsPage(AppState app) : base(app)
    {
        _addBrowser = new PillButton("Sign in with browser", IconKind.Globe)
        {
            Kind = ButtonKind.Primary,
            Height = 38,
            Width = 230
        };
        _addBrowser.Click += (_, _) => OpenBrowser();

        _addCookie = new PillButton("Use cookie", IconKind.Copy) { Kind = ButtonKind.Ghost, Height = 38, Width = 132 };
        _addCookie.Click += async (_, _) => await AddByCookieAsync();

        _refresh = new PillButton("Validate", IconKind.Refresh) { Kind = ButtonKind.Ghost, Height = 38, Width = 112 };
        _refresh.Click += async (_, _) => await ValidateAllAsync();

        _search = new TextField("Search accounts");
        _search.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Rebuild();
        };

        _place = new TextField("Place ID");
        if (app.Settings.DefaultPlaceId > 0)
        {
            _place.Text = app.Settings.DefaultPlaceId.ToString();
        }

        _recent = new PillButton("Recent", IconKind.Folder) { Kind = ButtonKind.Ghost, Height = 38, Width = 92 };
        _recent.Click += (_, _) => ShowRecentMenu();

        _launch = new PillButton("Launch selected", IconKind.Play) { Kind = ButtonKind.Primary, Height = 38, Width = 172 };
        _launch.Click += async (_, _) => await LaunchSelectionAsync();

        _more = new PillButton("...") { Kind = ButtonKind.Ghost, Height = 38, Width = 44 };
        _more.Click += (_, _) => ShowActionsMenu();

        _count = new Label
        {
            AutoSize = false,
            Font = Theme.Font(8.9f),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop };
        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(_place);
        toolbar.Controls.Add(_recent);
        toolbar.Controls.Add(_count);
        toolbar.Controls.Add(_more);
        toolbar.Controls.Add(_launch);
        toolbar.Resize += (_, _) => LayoutToolbar(toolbar);

        _list = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop, AutoScroll = true };
        _list.HandleCreated += (_, _) => Theme.ApplyDarkScrollbar(_list);
        _list.Resize += (_, _) => LayoutCards();
        _list.Paint += (_, e) => PaintEmptyState(e.Graphics);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Backdrop,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_list, 0, 1);
        Controls.Add(root);

        App.AccountsChanged += (_, _) => Rebuild();
        App.PresenceChanged += (_, _) => ApplyPresence();
        BrowserWindow.Instance.AccountAdded += (_, _) => Rebuild();

        Rebuild();
    }

    public override string NavLabel => "Accounts";

    public override IconKind NavIcon => IconKind.Users;

    public override string Heading => "Accounts";

    public override string Subheading => "Windows DPAPI / local";

    public override IReadOnlyList<Control> Actions => new Control[] { _addBrowser, _addCookie, _refresh };

    public event EventHandler? OpenServersRequested;

    public override void Activated()
    {
        Rebuild();
        AnimateEmpty();
    }

    private void AnimateEmpty()
    {
        EmptyProgress = 0f;
        UiAnimator.Animate(this, "empty", 0f, 1f, 280,
            Value => { EmptyProgress = Value; _list.Invalidate(); }, Theme.EaseOutCubic);
    }

    private void LayoutToolbar(Panel toolbar)
    {
        var width = toolbar.ClientSize.Width;
        _search.SetBounds(0, 9, Math.Min(290, Math.Max(150, width / 4)), 38);
        _place.SetBounds(_search.Right + 8, 9, 140, 38);
        _recent.SetBounds(_place.Right + 6, 9, 92, 38);

        _launch.Location = new Point(width - _launch.Width, 9);
        _more.Location = new Point(_launch.Left - 6 - _more.Width, 9);
        var CountLeft = _recent.Right + 12;
        var CountRight = _more.Left - 10;
        _count.SetBounds(CountLeft, 0, Math.Max(80, CountRight - CountLeft), 56);
    }

    private void PaintEmptyState(Graphics g)
    {
        if (_cards.Values.Any(card => card.Visible))
        {
            return;
        }

        Theme.EnableQuality(g);
        var IsEmpty = App.Store.Count == 0;
        var Title = IsEmpty ? "No accounts yet" : "Nothing matches that search";
        var Hint = IsEmpty
            ? "Sign in with browser to add an account. The browser session is cleared after import."
            : "Try another name, handle, tag, note or user ID.";

        var ContentWidth = Math.Max(240, _list.ClientSize.Width - 80);
        var OffsetY = (int)Math.Round((1f - EmptyProgress) * 10f);
        var CenterY = Math.Max(60, _list.ClientSize.Height / 2 - 40) + OffsetY;
        var TitleColor = Theme.Lerp(Theme.Muted, Theme.Text, EmptyProgress);
        var HintColor = Theme.Lerp(Theme.Faint, Theme.Muted, EmptyProgress);

        Theme.DrawText(g, Title, Theme.Font(13f, FontStyle.Bold), TitleColor, new Rectangle(40, CenterY, ContentWidth, 30),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.DrawText(g, Hint, Theme.Font(9.2f), HintColor, new Rectangle(40, CenterY + 34, ContentWidth, 66),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private void Rebuild()
    {
        var query = _search.Text.Trim();
        var accounts = App.Store.Accounts
            .Where(a => query.Length == 0
                        || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || a.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (a.Tag?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (a.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        || a.UserId.ToString().Contains(query, StringComparison.Ordinal))
            .ToList();

        foreach (var stale in _cards.Keys.Except(App.Store.Accounts.Select(a => a.Id)).ToList())
        {
            _cards[stale].Dispose();
            _cards.Remove(stale);
        }

        foreach (var account in accounts)
        {
            if (!_cards.TryGetValue(account.Id, out var card))
            {
                card = new AccountCard(account);
                Wire(card);
                _cards[account.Id] = card;
                _list.Controls.Add(card);
                _ = LoadAvatarAsync(card);
            }

            card.SetPresence(App.Presence.TryGetValue(account.UserId, out var presence) ? presence : null);
            card.Selected = App.SelectedUserIds.Contains(account.UserId);
            card.RefreshFromAccount();
            card.Visible = true;
        }

        foreach (var card in _cards.Values)
        {
            if (!accounts.Contains(card.Account))
            {
                card.Visible = false;
            }
        }

        LayoutCards();
        UpdateCounts();
        _list.Invalidate();
    }

    private void LayoutCards()
    {
        var y = 2;
        var width = Math.Max(420, _list.ClientSize.Width - 20);
        foreach (var account in App.Store.Accounts)
        {
            if (!_cards.TryGetValue(account.Id, out var card) || !card.Visible)
            {
                continue;
            }

            card.SetBounds(0, y, width, 64);
            y += 72;
        }

        _list.AutoScrollMinSize = new Size(0, y + 2);
        _list.HorizontalScroll.Enabled = false;
        _list.HorizontalScroll.Visible = false;
    }

    private void UpdateCounts()
    {
        var visible = _cards.Values.Count(card => card.Visible);
        var total = App.Store.Count;
        var selected = App.SelectedUserIds.Count;
        var shown = visible == total ? $"{total} account{(total == 1 ? "" : "s")}" : $"{visible} of {total} shown";
        _count.Text = shown;

        var hasAccounts = total > 0;
        _launch.Visible = hasAccounts;
        _count.Visible = hasAccounts;
        _addBrowser.Kind = hasAccounts ? ButtonKind.Subtle : ButtonKind.Primary;
    }

    private void Wire(AccountCard card)
    {
        card.SelectedChanged += (_, _) =>
        {
            if (card.Selected)
            {
                App.SelectedUserIds.Add(card.Account.UserId);
            }
            else
            {
                App.SelectedUserIds.Remove(card.Account.UserId);
            }

            UpdateCounts();
        };

        card.LaunchRequested += async (_, _) => await LaunchAsync(new[] { card.Account });
        card.ServersRequested += (_, _) => OpenServersRequested?.Invoke(this, EventArgs.Empty);
        card.BrowserRequested += (_, _) => BrowserWindow.Instance.EnterBrowserMode(card.Account);
        card.CopyCookieRequested += (_, _) => CopyCookie(card.Account);
        card.CopyUserIdRequested += (_, _) =>
        {
            SafeCopy(card.Account.UserId.ToString());
            App.Toast("User ID copied.", ToastKind.Success);
        };
        card.ToggleEnabledRequested += (_, _) =>
        {
            card.Account.Enabled = !card.Account.Enabled;
            App.Store.Save();
            card.RefreshFromAccount();
            App.Toast(card.Account.Enabled
                ? $"{card.Account.Name} will be included in batch launches."
                : $"{card.Account.Name} is muted for batch launches.", ToastKind.Info);
        };
        card.TagRequested += (_, _) => EditTag(card.Account);
        card.NotesRequested += (_, _) => EditNotes(card.Account);
        card.RemoveRequested += (_, _) => RemoveAccount(card.Account);
    }

    private void OpenBrowser()
    {
        BrowserWindow.Instance.Login();
    }

    private void ShowRecentMenu()
    {
        var menu = DarkMenu.Create();
        var places = App.Settings.RecentPlaces ?? new List<RecentPlace>();
        if (places.Count == 0)
        {
            var empty = DarkMenu.Item("No recent places yet", () => { });
            empty.Enabled = false;
            menu.Items.Add(empty);
        }
        else
        {
            foreach (var place in places.OrderByDescending(p => p.UsedUtc).Take(8))
            {
                var label = string.IsNullOrWhiteSpace(place.Name) ? $"Place {place.PlaceId}" : place.Name;
                menu.Items.Add(DarkMenu.Item($"{label}  ·  {place.PlaceId}", () =>
                {
                    _place.Text = place.PlaceId.ToString();
                    App.Toast($"{label} is now the join target.", ToastKind.Info);
                }));
            }
        }

        menu.Show(_recent, new Point(0, _recent.Height + 4));
    }

    private void ShowActionsMenu()
    {
        var menu = DarkMenu.Create();
        menu.Items.Add(DarkMenu.Item("Select all", ToggleSelectAll));
        menu.Items.Add(DarkMenu.Item("Clear selection", () =>
        {
            App.SelectedUserIds.Clear();
            foreach (var card in _cards.Values)
            {
                card.Selected = false;
            }

            UpdateCounts();
        }));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Include selected in batches", () => SetSelectedEnabled(true)));
        menu.Items.Add(DarkMenu.Item("Mute selected", () => SetSelectedEnabled(false)));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Import cookies", () => _ = ImportCookiesAsync()));
        menu.Items.Add(DarkMenu.Separator());
        menu.Items.Add(DarkMenu.Item("Remove selected", () => _ = RemoveSelectedAsync(), danger: true));
        menu.Show(_more, new Point(_more.Width - 200, _more.Height + 4));
    }

    private List<Account> SelectedAccounts(bool includeMuted = false) =>
        App.Store.Accounts.Where(a => App.SelectedUserIds.Contains(a.UserId) && (includeMuted || a.Enabled)).ToList();

    private void SetSelectedEnabled(bool enabled)
    {
        var targets = SelectedAccounts(includeMuted: true);
        if (targets.Count == 0)
        {
            App.Toast("Select at least one account first.", ToastKind.Warning);
            return;
        }

        App.Store.SetEnabled(targets, enabled);
        App.NotifyAccountsChanged();
        App.Toast(enabled ? $"{targets.Count} account(s) included in batches." : $"{targets.Count} account(s) muted.",
            ToastKind.Success);
    }

    private async Task RemoveSelectedAsync()
    {
        var targets = SelectedAccounts(includeMuted: true);
        if (targets.Count == 0)
        {
            App.Toast("Select at least one account first.", ToastKind.Warning);
            return;
        }

        if (!Dialogs.Confirm(this, "Remove accounts",
                $"Remove {targets.Count} account(s)? Their stored sessions are deleted from this machine.", "Remove"))
        {
            return;
        }

        foreach (var target in targets)
        {
            App.SelectedUserIds.Remove(target.UserId);
        }

        App.Store.RemoveMany(targets);
        App.NotifyAccountsChanged();
        App.Toast($"{targets.Count} account(s) removed.", ToastKind.Info);
        await Task.CompletedTask;
    }

    private async Task RememberPlaceAsync(long placeId)
    {
        try
        {
            var info = await App.Api.GetGameInfoAsync(placeId);
            var list = App.Settings.RecentPlaces ??= new List<RecentPlace>();
            list.RemoveAll(place => place.PlaceId == placeId);
            list.Insert(0, new RecentPlace
            {
                PlaceId = placeId,
                Name = info?.Name ?? $"Place {placeId}",
                UsedUtc = DateTime.UtcNow
            });

            if (list.Count > 10)
            {
                list.RemoveRange(10, list.Count - 10);
            }

            App.Settings.Save();
        }
        catch (Exception)
        {
            // Recent places are a convenience; a failed lookup must not disturb a launch.
        }
    }

    private void CopyCookie(Account account)
    {
        var cookie = App.Store.CookieFor(account);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            App.Toast("That session is not readable on this Windows account.", ToastKind.Warning);
            return;
        }

        SafeCopy(cookie);
        App.Toast("Cookie copied to the clipboard.", ToastKind.Success);
    }

    private static void SafeCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // The clipboard can be locked by another process.
        }
    }

    private async Task LoadAvatarAsync(AccountCard card)
    {
        var image = await App.Avatars.GetAsync(card.Account.UserId);
        if (IsDisposed || card.IsDisposed)
        {
            return;
        }

        card.SetAvatar(image);
        if (image is not null)
        {
            return;
        }

        // One late retry covers a slow thumbnail render without blocking the card.
        await Task.Delay(4000);
        if (IsDisposed || card.IsDisposed)
        {
            return;
        }

        image = await App.Avatars.GetAsync(card.Account.UserId);
        if (!IsDisposed && !card.IsDisposed)
        {
            card.SetAvatar(image);
        }
    }

    private void ApplyPresence()
    {
        foreach (var card in _cards.Values)
        {
            card.SetPresence(App.Presence.TryGetValue(card.Account.UserId, out var presence) ? presence : null);
        }
    }

    private void ToggleSelectAll()
    {
        var visible = _cards.Values.Where(card => card.Visible).ToList();
        var allSelected = visible.Count > 0 && visible.All(card => card.Selected);
        foreach (var card in visible)
        {
            card.Selected = !allSelected;
        }

        UpdateCounts();
    }

    private long PlaceId()
    {
        var digits = new string(_place.Text.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var id) && id > 0 ? id : App.Settings.DefaultPlaceId;
    }

    private async Task LaunchSelectionAsync()
    {
        var targets = SelectedAccounts().ToList();
        if (targets.Count == 0)
        {
            App.Toast("Select at least one account first.", ToastKind.Warning);
            return;
        }

        await LaunchAsync(targets);
    }

    private async Task LaunchAsync(IReadOnlyList<Account> accounts)
    {
        if (_busy)
        {
            return;
        }

        var placeId = PlaceId();
        if (placeId <= 0)
        {
            App.Toast("Set a join place ID first.", ToastKind.Warning);
            _place.FocusInput();
            return;
        }

        var targets = accounts.Where(a => a.Enabled).ToList();
        if (targets.Count == 0)
        {
            App.Toast("Those accounts are muted.", ToastKind.Warning);
            return;
        }

        App.Settings.DefaultPlaceId = placeId;
        App.Settings.Save();

        _busy = true;
        _launch.Busy = true;
        try
        {
            var summary = await App.LaunchAsync(targets, placeId, null);
            if (summary.Failed == 0)
            {
                App.Toast($"Launched {summary.Launched} client(s) into {placeId}.", ToastKind.Success);
            }
            else
            {
                App.Toast($"{summary.Launched} launched, {summary.Failed} failed. {summary.Errors.FirstOrDefault()}",
                    ToastKind.Warning);
            }

            _ = RememberPlaceAsync(placeId);
            await App.RefreshPresenceAsync();
        }
        catch (Exception ex)
        {
            App.Toast(ex.Message, ToastKind.Error);
        }
        finally
        {
            _busy = false;
            _launch.Busy = false;
        }
    }

    private async Task AddByCookieAsync()
    {
        var raw = Dialogs.Ask(this,
            "Add an account",
            "Paste the .ROBLOSECURITY value: DevTools → Application → Cookies → roblox.com. The cookie is validated and then encrypted locally.",
            "Paste cookie", secret: true, okText: "Check and add");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var cookie = AccountStore.NormalizeCookie(raw);
        if (cookie is null)
        {
            App.Toast("That does not look like a Roblox cookie.", ToastKind.Error);
            return;
        }

        try
        {
            App.Toast("Checking the session with Roblox…", ToastKind.Info);
            var user = await App.Api.GetAuthenticatedAsync(cookie);
            if (user is null)
            {
                App.Toast("Roblox rejected that cookie.", ToastKind.Error);
                return;
            }

            var existed = App.Store.FindByUserId(user.Id) is not null;
            App.Store.AddOrUpdate(user, cookie);
            App.SelectedUserIds.Add(user.Id);
            App.Avatars.Forget(user.Id);
            App.NotifyAccountsChanged();
            await App.RefreshPresenceAsync();
            App.Toast(existed ? $"Session refreshed for {user.DisplayName}." : $"Added {user.DisplayName}.",
                ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Toast(ex.Message, ToastKind.Error);
        }
    }

    private async Task ImportCookiesAsync()
    {
        var text = Dialogs.AskMultiline(this, "Import cookies",
            "One .ROBLOSECURITY value per line. Each session is checked with Roblox before it is stored.",
            "Paste one cookie per line", "Import", 150);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        var refreshed = 0;
        var rejected = 0;

        foreach (var line in lines)
        {
            var cookie = AccountStore.NormalizeCookie(line);
            if (cookie is null || !seen.Add(cookie))
            {
                continue;
            }

            try
            {
                var user = await App.Api.GetAuthenticatedAsync(cookie);
                if (user is null)
                {
                    rejected++;
                    continue;
                }

                var existed = App.Store.FindByUserId(user.Id) is not null;
                App.Store.AddOrUpdate(user, cookie);
                App.Avatars.Forget(user.Id);
                if (existed)
                {
                    refreshed++;
                }
                else
                {
                    added++;
                }
            }
            catch (Exception)
            {
                rejected++;
            }
        }

        App.NotifyAccountsChanged();
        await App.RefreshPresenceAsync();
        App.Toast($"{added} added, {refreshed} refreshed, {rejected} rejected.",
            added + refreshed > 0 ? ToastKind.Success : ToastKind.Warning);
    }

    private async Task ValidateAllAsync()
    {
        if (App.Store.Count == 0)
        {
            App.Toast("There are no accounts to check.", ToastKind.Warning);
            return;
        }

        _refresh.Busy = true;
        try
        {
            var expired = new List<Account>();
            foreach (var account in App.Store.Accounts.ToList())
            {
                var cookie = App.Store.CookieFor(account);
                if (cookie is null)
                {
                    expired.Add(account);
                    continue;
                }

                var user = await App.Api.GetAuthenticatedAsync(cookie);
                if (user is null)
                {
                    expired.Add(account);
                    continue;
                }

                if (user.Name != account.Username || user.DisplayName != account.DisplayName)
                {
                    account.Username = user.Name;
                    account.DisplayName = user.DisplayName;
                    App.Avatars.Forget(account.UserId);
                }
            }

            App.Store.Save();
            App.NotifyAccountsChanged();
            await App.RefreshPresenceAsync();

            if (expired.Count == 0)
            {
                App.Toast("Every session is still valid.", ToastKind.Success);
            }
            else
            {
                App.Toast($"{expired.Count} expired: {string.Join(", ", expired.Select(a => a.Name))}", ToastKind.Warning);
            }
        }
        catch (Exception ex)
        {
            App.Toast(ex.Message, ToastKind.Error);
        }
        finally
        {
            _refresh.Busy = false;
        }
    }

    private void EditTag(Account account)
    {
        var tag = Dialogs.Ask(this, "Account tag", "A short label used for searching, e.g. main, alt, group.",
            account.Tag ?? "", okText: "Save tag");
        if (tag is null)
        {
            return;
        }

        account.Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
        App.Store.Save();
        App.NotifyAccountsChanged();
        App.Toast("Tag saved.", ToastKind.Success);
    }

    private void EditNotes(Account account)
    {
        var notes = Dialogs.AskMultiline(this, $"Notes for {account.Name}",
            "Stored locally with the account. Never sent to Roblox.", account.Notes ?? "", "Save notes", 140);
        if (notes is null)
        {
            return;
        }

        account.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        App.Store.Save();
        App.NotifyAccountsChanged();
        App.Toast("Notes saved.", ToastKind.Success);
    }

    private void RemoveAccount(Account account)
    {
        if (!Dialogs.Confirm(this, "Remove account",
                $"Remove {account.Name}? The stored session is deleted from this machine.", "Remove"))
        {
            return;
        }

        App.SelectedUserIds.Remove(account.UserId);
        App.Store.Remove(account);
        App.NotifyAccountsChanged();
        App.Toast($"{account.Name} removed.", ToastKind.Info);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }
}
