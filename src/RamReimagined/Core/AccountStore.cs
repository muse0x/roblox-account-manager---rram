using System.Text.Json;
using System.Text.Json.Serialization;

namespace RamReimagined.Core;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public string Username { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>DPAPI-protected .ROBLOSECURITY value.</summary>
    public string ProtectedCookie { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastLaunchedUtc { get; set; }

    public string? Tag { get; set; }

    /// <summary>Local-only note shown on the account card. Never sent to Roblox.</summary>
    public string? Notes { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim();
            return source.Length == 1 ? source.ToUpperInvariant() : source[..1].ToUpperInvariant();
        }
    }
}

internal sealed class AccountFile
{
    public int Version { get; set; } = 2;

    public List<Account> Accounts { get; set; } = new();
}

public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly List<Account> _accounts = new();

    public IReadOnlyList<Account> Accounts => _accounts;

    public int Count => _accounts.Count;

    public event EventHandler? Changed;

    public void Load()
    {
        _accounts.Clear();
        try
        {
            if (!File.Exists(AppPaths.AccountsFile))
            {
                return;
            }

            var file = JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(AppPaths.AccountsFile), JsonOptions);
            if (file?.Accounts is not null)
            {
                _accounts.AddRange(file.Accounts);
            }
        }
        catch (Exception)
        {
            // Corrupt store: start empty rather than crash, the file is left alone for recovery.
        }
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        var file = new AccountFile { Accounts = _accounts };
        File.WriteAllText(AppPaths.AccountsFile, JsonSerializer.Serialize(file, JsonOptions));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string? NormalizeCookie(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        const string prefix = ".ROBLOSECURITY=";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }

        var semi = value.IndexOf(';');
        if (semi >= 0)
        {
            value = value[..semi];
        }

        value = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string? CookieFor(Account account) => Protection.Unprotect(account.ProtectedCookie);

    public Account? FindByUserId(long userId) => _accounts.FirstOrDefault(a => a.UserId == userId);

    public Account AddOrUpdate(AuthUser user, string cookie, string? tag = null)
    {
        var existing = FindByUserId(user.Id);
        if (existing is null)
        {
            existing = new Account
            {
                UserId = user.Id,
                Username = user.Name,
                DisplayName = user.DisplayName,
                Tag = tag
            };
            _accounts.Add(existing);
        }

        existing.Username = user.Name;
        existing.DisplayName = user.DisplayName;
        existing.ProtectedCookie = Protection.Protect(cookie);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            existing.Tag = tag;
        }

        Save();
        return existing;
    }

    public void UpdateCookie(Account account, string cookie)
    {
        account.ProtectedCookie = Protection.Protect(cookie);
        Save();
    }

    public void TouchLaunched(Account account)
    {
        account.LastLaunchedUtc = DateTime.UtcNow;
        Save();
    }

    public void Remove(Account account)
    {
        _accounts.Remove(account);
        Save();
    }

    public void RemoveMany(IEnumerable<Account> accounts)
    {
        foreach (var account in accounts.ToList())
        {
            _accounts.Remove(account);
        }

        Save();
    }

    public void SetEnabled(IEnumerable<Account> accounts, bool enabled)
    {
        foreach (var account in accounts)
        {
            account.Enabled = enabled;
        }

        Save();
    }
}
