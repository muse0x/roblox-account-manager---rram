using RamReimagined.Core;

namespace RamReimagined.UI;

internal sealed class ServersPage : PageView
{
    private readonly TextField _place;
    private readonly PillButton _load;
    private readonly PillButton _reload;
    private readonly Label _gameLabel;
    private readonly Panel _list;
    private readonly List<ServerRow> _rows = new();
    private GameInfo? _game;

    public ServersPage(AppState app) : base(app)
    {
        _place = new TextField("Place ID");
        if (app.Settings.DefaultPlaceId > 0)
        {
            _place.Text = app.Settings.DefaultPlaceId.ToString();
        }

        _load = new PillButton("Load servers", IconKind.Search) { Kind = ButtonKind.Primary, Width = 150, Height = 38 };
        _load.Click += async (_, _) => await LoadAsync();

        _reload = new PillButton("Reload", IconKind.Refresh) { Kind = ButtonKind.Subtle, Width = 110, Height = 38 };
        _reload.Click += async (_, _) => await LoadAsync();

        _gameLabel = new Label
        {
            AutoSize = false,
            Font = Theme.Font(8.75f),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Load a place to list its public servers."
        };

        var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop };
        toolbar.Controls.Add(_place);
        toolbar.Controls.Add(_load);
        toolbar.Controls.Add(_gameLabel);
        toolbar.Resize += (_, _) =>
        {
            _place.SetBounds(0, 9, 180, 38);
            _load.Location = new Point(_place.Right + 10, 9);
            _gameLabel.SetBounds(_load.Right + 16, 0,
                Math.Max(80, toolbar.ClientSize.Width - _load.Right - 20), 56);
        };

        _list = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Backdrop, AutoScroll = true };
        _list.HandleCreated += (_, _) => Theme.ApplyDarkScrollbar(_list);
        _list.Resize += (_, _) => LayoutRows();
        _list.Paint += (_, e) => PaintEmpty(e.Graphics);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Backdrop,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(new ServerTableHeader { Dock = DockStyle.Fill }, 0, 1);
        root.Controls.Add(_list, 0, 2);
        Controls.Add(root);
    }

    public override string NavLabel => "Servers";

    public override IconKind NavIcon => IconKind.Server;

    public override string Heading => "Server browser";

    public override string Subheading => "Pick a live server and drop every selected account straight into it.";

    public override IReadOnlyList<Control> Actions => new Control[] { _reload };

    public override void Activated()
    {
        if (App.Settings.DefaultPlaceId > 0 && string.IsNullOrWhiteSpace(_place.Text))
        {
            _place.Text = App.Settings.DefaultPlaceId.ToString();
        }
    }

    private void PaintEmpty(Graphics g)
    {
        if (_rows.Count > 0)
        {
            return;
        }

        Theme.EnableQuality(g);
        var width = Math.Max(240, _list.ClientSize.Width - 80);
        var centerY = Math.Max(40, _list.ClientSize.Height / 2 - 30);
        Theme.DrawText(g, "No servers loaded", Theme.Font(12.5f, FontStyle.Bold), Theme.Muted,
            new Rectangle(40, centerY, width, 24),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.DrawText(g, "Enter a place ID and press Load servers. Only public servers are listed.",
            Theme.Font(9f), Theme.Faint, new Rectangle(40, centerY + 28, width, 36),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    private long PlaceId()
    {
        var digits = new string(_place.Text.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var id) && id > 0 ? id : App.Settings.DefaultPlaceId;
    }

    private async Task LoadAsync()
    {
        var placeId = PlaceId();
        if (placeId <= 0)
        {
            App.Toast("Enter a place ID first.", ToastKind.Warning);
            _place.FocusInput();
            return;
        }

        _load.Busy = true;
        _reload.Busy = true;
        try
        {
            _gameLabel.Text = "Loading place…";
            _game = await App.Api.GetGameInfoAsync(placeId);
            _gameLabel.Text = _game is null
                ? $"Place {placeId} · no universe information returned."
                : $"{_game.Name}  ·  {_game.CreatorName}  ·  {_game.Playing:N0} playing  ·  max {_game.MaxPlayers}";

            var cookie = App.Store.Accounts.Select(App.Store.CookieFor)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            var servers = await App.Api.GetPublicServersAsync(cookie, placeId, null,
                Math.Clamp(App.Settings.ServerPageSize, 10, 100));

            FillRows(servers);
            App.Toast(servers.Count == 0
                ? "No public servers came back for that place."
                : $"Loaded {servers.Count} public servers.", servers.Count == 0 ? ToastKind.Warning : ToastKind.Success);
        }
        catch (Exception ex)
        {
            App.Toast(ex.Message, ToastKind.Error);
        }
        finally
        {
            _load.Busy = false;
            _reload.Busy = false;
        }
    }

    private void FillRows(IReadOnlyList<PublicServer> servers)
    {
        foreach (var row in _rows)
        {
            row.Dispose();
        }

        _rows.Clear();
        _list.Controls.Clear();

        foreach (var server in servers)
        {
            var row = new ServerRow(server);
            row.JoinRequested += async (_, _) => await JoinAsync(server);
            _rows.Add(row);
            _list.Controls.Add(row);
        }

        LayoutRows();
        _list.Invalidate();
    }

    private void LayoutRows()
    {
        var y = 2;
        var width = Math.Max(420, _list.ClientSize.Width - 20);
        foreach (var row in _rows)
        {
            row.SetBounds(0, y, width, 58);
            y += 64;
        }

        _list.AutoScrollMinSize = new Size(0, y + 2);
        _list.HorizontalScroll.Enabled = false;
        _list.HorizontalScroll.Visible = false;
    }

    private async Task JoinAsync(PublicServer server)
    {
        var targets = App.SelectedAccounts().ToList();
        if (targets.Count == 0)
        {
            App.Toast("Select accounts on the Accounts page first.", ToastKind.Warning);
            return;
        }

        if (_game is null)
        {
            App.Toast("Load a place before joining.", ToastKind.Warning);
            return;
        }

        App.Toast($"Joining {server.ShortId} with {targets.Count} account(s)…", ToastKind.Info);
        try
        {
            var summary = await App.LaunchAsync(targets, _game.PlaceId, server.Id);
            App.Toast(summary.Failed == 0
                    ? $"Joined {server.ShortId} with {summary.Launched} account(s)."
                    : $"{summary.Launched} joined, {summary.Failed} failed. {summary.Errors.FirstOrDefault()}",
                summary.Failed == 0 ? ToastKind.Success : ToastKind.Warning);
        }
        catch (Exception ex)
        {
            App.Toast(ex.Message, ToastKind.Error);
        }
    }
}
