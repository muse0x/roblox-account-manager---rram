using RamReimagined.Core;

namespace RamReimagined.UI;

/// <summary>One place for the API client, the encrypted store, settings and presence.</summary>
internal sealed class AppState : IDisposable
{
    public AppState()
    {
        Settings = AppSettings.Load();
        Theme.SetAccent(Settings.AccentName);

        Api = new RobloxApi();
        Store = new AccountStore();
        Store.Load();
        Avatars = new AvatarCache(Api);
        Launcher = new RobloxLauncher(Api, Store, Settings);

        if (Settings.KeepMultiInstance)
        {
            Launcher.MultiInstance.Enable();
        }

        Current = this;
    }

    public static AppState Current { get; private set; } = null!;

    public RobloxApi Api { get; }

    public AccountStore Store { get; }

    public AppSettings Settings { get; }

    public RobloxLauncher Launcher { get; }

    public AvatarCache Avatars { get; }

    public Dictionary<long, PresenceInfo> Presence { get; private set; } = new();

    public HashSet<long> SelectedUserIds { get; } = new();

    public event EventHandler? PresenceChanged;

    public event EventHandler? AccountsChanged;

    public event Action<string, ToastKind>? ToastRequested;

    public void Toast(string message, ToastKind kind = ToastKind.Info) => ToastRequested?.Invoke(message, kind);

    public void NotifyAccountsChanged() => AccountsChanged?.Invoke(this, EventArgs.Empty);

    public IEnumerable<Account> SelectedAccounts() =>
        Store.Accounts.Where(a => SelectedUserIds.Contains(a.UserId) && a.Enabled);

    public void SetAccent(string name)
    {
        Settings.AccentName = name;
        Theme.SetAccent(name);
        Settings.Save();
    }

    public async Task RefreshPresenceAsync(CancellationToken ct = default)
    {
        if (Store.Count == 0)
        {
            Presence = new Dictionary<long, PresenceInfo>();
            PresenceChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var cookie = Store.Accounts
            .Select(Store.CookieFor)
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        if (string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }

        var ids = Store.Accounts.Where(a => a.UserId > 0).Select(a => a.UserId).Distinct().ToList();
        var presence = await Api.GetPresenceAsync(cookie!, ids, ct).ConfigureAwait(false);
        Presence = presence.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First());
        PresenceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<LaunchSummary> LaunchAsync(IReadOnlyList<Account> accounts, long placeId, string? jobId,
        Action<string>? report = null, CancellationToken ct = default)
    {
        var launched = 0;
        var errors = new List<string>();

        if (Settings.LowGraphics)
        {
            RobloxSettings.TrySetGraphicsQuality(1, out _);
        }

        for (var index = 0; index < accounts.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var account = accounts[index];
            var result = await Launcher.LaunchAsync(account, placeId, jobId, ct).ConfigureAwait(false);
            if (result.Ok)
            {
                launched++;
            }
            else
            {
                errors.Add($"{account.Name}: {result.Message}");
            }

            report?.Invoke(result.Message);

            if (index < accounts.Count - 1 && Settings.LaunchStaggerSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Settings.LaunchStaggerSeconds), ct).ConfigureAwait(false);
            }
        }

        if (Settings.TileWindows && launched > 0)
        {
            WindowTiler.Tile();
        }

        return new LaunchSummary(launched, errors.Count, errors);
    }

    public void Dispose()
    {
        Launcher.MultiInstance.Dispose();
        Api.Dispose();
    }
}

public sealed record LaunchSummary(int Launched, int Failed, IReadOnlyList<string> Errors);
