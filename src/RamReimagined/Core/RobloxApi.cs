using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RamReimagined.Core;

public sealed record AuthUser(long Id, string Name, string DisplayName);

public sealed record PresenceInfo(long UserId, int Type, string? LastLocation, long? PlaceId, string? GameId, long? UniverseId, DateTimeOffset? LastOnline)
{
    public string State => Type switch
    {
        0 => "Offline",
        1 => "Website",
        2 => "In game",
        3 => "In Studio",
        _ => "Unknown"
    };

    public string Detail => Type switch
    {
        2 => string.IsNullOrWhiteSpace(LastLocation) ? "Playing" : LastLocation!,
        1 => "Browsing roblox.com",
        3 => "Studio session",
        _ => LastOnline is null ? "No session" : $"Last seen {Relative(LastOnline.Value)}"
    };

    private static string Relative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when;
        if (delta.TotalMinutes < 1) return "just now";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }
}

public sealed record AuthTicket(string Ticket, string Cookie);

public sealed record PublicServer(string Id, int Playing, int MaxPlayers, int Ping, double Fps, IReadOnlyList<string> PlayerTokens)
{
    public double Fill => MaxPlayers <= 0 ? 0 : Math.Clamp((double)Playing / MaxPlayers, 0, 1);

    public string ShortId => Id.Length <= 8 ? Id : Id[..8];
}

public sealed record GameInfo(long PlaceId, long UniverseId, string Name, string CreatorName, int Playing, long Visits, int MaxPlayers, long FavoritedCount);

/// <summary>
/// Thin client over the endpoints Roblox still exposes today. Every request is
/// authenticated per-account with a .ROBLOSECURITY cookie (no shared jar), and
/// POSTs transparently recover from the CSRF challenge.
/// </summary>
public sealed class RobloxApi : IDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _csrfByCookie = new();

    public RobloxApi()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public void Dispose() => _http.Dispose();

    private static HttpRequestMessage Request(HttpMethod method, string url, string? cookie)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
        }

        return request;
    }

    /// <summary>Validates a cookie and returns the account it belongs to, or null when the session is dead.</summary>
    public async Task<AuthUser?> GetAuthenticatedAsync(string cookie, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var id))
        {
            return null;
        }

        return new AuthUser(id.GetInt64(), GetString(root, "name") ?? "", GetString(root, "displayName") ?? "");
    }

    public async Task<IReadOnlyList<PresenceInfo>> GetPresenceAsync(string cookie, IReadOnlyList<long> userIds, CancellationToken ct = default)
    {
        var result = new List<PresenceInfo>();
        if (userIds.Count == 0)
        {
            return result;
        }

        using var doc = await PostJsonAsync("https://presence.roblox.com/v1/presence/users", cookie,
            new { userIds = userIds.ToArray() }, ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("userPresences", out var presences))
        {
            return result;
        }

        foreach (var item in presences.EnumerateArray())
        {
            result.Add(new PresenceInfo(
                item.TryGetProperty("userId", out var uid) ? uid.GetInt64() : 0,
                item.TryGetProperty("userPresenceType", out var type) ? type.GetInt32() : 0,
                GetString(item, "lastLocation"),
                item.TryGetProperty("placeId", out var placeId) && placeId.ValueKind == JsonValueKind.Number ? placeId.GetInt64() : null,
                GetString(item, "gameId"),
                item.TryGetProperty("universeId", out var universe) && universe.ValueKind == JsonValueKind.Number ? universe.GetInt64() : null,
                item.TryGetProperty("lastOnline", out var lastOnline) && lastOnline.ValueKind == JsonValueKind.String
                    ? lastOnline.GetDateTimeOffset()
                    : null));
        }

        return result;
    }

    /// <summary>Trades a cookie for a fresh client authentication ticket. Returns null when Roblox rejects it.</summary>
    public async Task<AuthTicket?> GetAuthTicketAsync(string cookie, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = Request(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket/", cookie);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
            if (_csrfByCookie.TryGetValue(cookie, out var csrf))
            {
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            RememberCsrf(cookie, response);

            if (response.StatusCode == HttpStatusCode.Forbidden && attempt < 2)
            {
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (!response.Headers.TryGetValues("rbx-authentication-ticket", out var tickets))
            {
                return null;
            }

            var ticket = tickets.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ticket))
            {
                return null;
            }

            var refreshed = ExtractRefreshedCookie(response) ?? cookie;
            return new AuthTicket(ticket, refreshed);
        }

        return null;
    }

    public async Task<GameInfo?> GetGameInfoAsync(long placeId, CancellationToken ct = default)
    {
        using var universeDoc = await GetJsonAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", null, ct).ConfigureAwait(false);
        if (universeDoc is null || !universeDoc.RootElement.TryGetProperty("universeId", out var universeElement))
        {
            return null;
        }

        var universeId = universeElement.GetInt64();
        using var gameDoc = await GetJsonAsync($"https://games.roblox.com/v1/games?universeIds={universeId}", null, ct).ConfigureAwait(false);
        if (gameDoc is null || !gameDoc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return new GameInfo(placeId, universeId, $"Place {placeId}", "Unknown", 0, 0, 0, 0);
        }

        var game = data[0];
        var creator = game.TryGetProperty("creator", out var c) ? GetString(c, "name") ?? "" : "";
        return new GameInfo(
            game.TryGetProperty("rootPlaceId", out var root) ? root.GetInt64() : placeId,
            universeId,
            GetString(game, "name") ?? $"Place {placeId}",
            creator,
            game.TryGetProperty("playing", out var playing) ? playing.GetInt32() : 0,
            game.TryGetProperty("visits", out var visits) ? visits.GetInt64() : 0,
            game.TryGetProperty("maxPlayers", out var max) ? max.GetInt32() : 0,
            game.TryGetProperty("favoritedCount", out var favs) ? favs.GetInt64() : 0);
    }

    public async Task<IReadOnlyList<PublicServer>> GetPublicServersAsync(string? cookie, long placeId, string? cursor, int limit, CancellationToken ct = default)
    {
        var servers = new List<PublicServer>();
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?limit={Math.Clamp(limit, 10, 100)}";
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        using var doc = await GetJsonAsync(url, cookie, ct).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data))
        {
            return servers;
        }

        foreach (var item in data.EnumerateArray())
        {
            var tokens = new List<string>();
            if (item.TryGetProperty("playerTokens", out var tokenArray) && tokenArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var token in tokenArray.EnumerateArray())
                {
                    if (token.ValueKind == JsonValueKind.String)
                    {
                        tokens.Add(token.GetString() ?? "");
                    }
                }
            }

            servers.Add(new PublicServer(
                GetString(item, "id") ?? "",
                item.TryGetProperty("playing", out var playing) ? playing.GetInt32() : 0,
                item.TryGetProperty("maxPlayers", out var max) ? max.GetInt32() : 0,
                item.TryGetProperty("ping", out var ping) ? ping.GetInt32() : 0,
                item.TryGetProperty("fps", out var fps) && fps.ValueKind == JsonValueKind.Number ? fps.GetDouble() : 0,
                tokens));
        }

        return servers;
    }

    public async Task<string?> GetNextServerCursorAsync(string? cookie, long placeId, string? cursor, int limit, CancellationToken ct = default)
    {
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?limit={Math.Clamp(limit, 10, 100)}";
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        using var doc = await GetJsonAsync(url, cookie, ct).ConfigureAwait(false);
        return doc is null ? null : GetString(doc.RootElement, "nextPageCursor");
    }

    public async Task<IReadOnlyList<PublicServer>> GetVipServersAsync(string? cookie, long universeId, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"https://games.roblox.com/v1/games/vip-servers/{universeId}", cookie, ct).ConfigureAwait(false);
        var servers = new List<PublicServer>();
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data))
        {
            return servers;
        }

        foreach (var item in data.EnumerateArray())
        {
            servers.Add(new PublicServer(
                GetString(item, "id") ?? "",
                item.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array ? players.GetArrayLength() : 0,
                item.TryGetProperty("maxPlayers", out var max) ? max.GetInt32() : 0,
                0, 0, Array.Empty<string>()));
        }

        return servers;
    }

    public async Task<string?> GetAvatarHeadshotUrlAsync(long userId, CancellationToken ct = default)
    {
        var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
        using var doc = await GetJsonAsync(url, null, ct).ConfigureAwait(false);
        return FirstImageUrl(doc);
    }

    public async Task<string?> GetAvatarBustUrlAsync(long userId, CancellationToken ct = default)
    {
        var url = $"https://thumbnails.roblox.com/v1/users/avatar-bust?userIds={userId}&size=150x150&format=Png&isCircular=false";
        using var doc = await GetJsonAsync(url, null, ct).ConfigureAwait(false);
        return FirstImageUrl(doc);
    }

    public async Task<string?> GetGameIconUrlAsync(long universeId, CancellationToken ct = default)
    {
        var url = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=150x150&format=Png&isCircular=false";
        using var doc = await GetJsonAsync(url, null, ct).ConfigureAwait(false);
        return FirstImageUrl(doc);
    }

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Get, url, null);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false) : null;
    }

    private static string? FirstImageUrl(JsonDocument? doc)
    {
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        var first = data[0];
        var state = GetString(first, "state");
        return state == "Completed" ? GetString(first, "imageUrl") : null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, string? cookie, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, url, cookie);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
    }

    private async Task<JsonDocument?> PostJsonAsync(string url, string cookie, object body, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = Request(HttpMethod.Post, url, cookie);
            request.Content = JsonContent.Create(body);
            request.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
            if (_csrfByCookie.TryGetValue(cookie, out var csrf))
            {
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            RememberCsrf(cookie, response);

            if (response.StatusCode == HttpStatusCode.Forbidden && attempt < 2)
            {
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
        }

        return null;
    }

    private void RememberCsrf(string cookie, HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-csrf-token", out var values))
        {
            var token = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token))
            {
                _csrfByCookie[cookie] = token;
            }
        }
    }

    private static string? ExtractRefreshedCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var header in cookies)
        {
            if (!header.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = header[".ROBLOSECURITY=".Length..];
            var semi = value.IndexOf(';');
            if (semi >= 0)
            {
                value = value[..semi];
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
