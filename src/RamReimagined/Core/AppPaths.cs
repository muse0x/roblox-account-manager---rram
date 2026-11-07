namespace RamReimagined.Core;

public static class AppPaths
{
    static AppPaths()
    {
        MigrateLegacyData();
    }

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RRAM");

    private static void MigrateLegacyData()
    {
        try
        {
            var AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var Legacy = Path.Combine(AppData, "RobloxAccountManagerReimagined");
            var Current = Path.Combine(AppData, "RRAM");
            if (!Directory.Exists(Legacy))
            {
                return;
            }

            Directory.CreateDirectory(Current);
            foreach (var Name in new[] { "accounts.json", "settings.json" })
            {
                var From = Path.Combine(Legacy, Name);
                var To = Path.Combine(Current, Name);
                if (File.Exists(From) && !File.Exists(To))
                {
                    File.Copy(From, To);
                }
            }

            var LegacyAvatars = Path.Combine(Legacy, "avatars");
            var CurrentAvatars = Path.Combine(Current, "avatars");
            if (!Directory.Exists(LegacyAvatars))
            {
                return;
            }

            Directory.CreateDirectory(CurrentAvatars);
            foreach (var Source in Directory.EnumerateFiles(LegacyAvatars))
            {
                var Destination = Path.Combine(CurrentAvatars, Path.GetFileName(Source));
                if (!File.Exists(Destination))
                {
                    File.Copy(Source, Destination);
                }
            }
        }
        catch (Exception)
        {
            // Migration is best-effort; an empty store is still a safe startup state.
        }
    }

    public static string AccountsFile => Path.Combine(DataDirectory, "accounts.json");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string AvatarCacheDirectory => Path.Combine(DataDirectory, "avatars");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AvatarCacheDirectory);
    }

    public static string LocalRobloxDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
}

public sealed class RecentPlace
{
    public long PlaceId { get; set; }

    public string Name { get; set; } = "";

    public DateTime UsedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public long DefaultPlaceId { get; set; }

    public int RefreshSeconds { get; set; } = 90;

    public double LaunchStaggerSeconds { get; set; } = 6;

    public string LaunchVersion { get; set; } = "";

    public bool KeepMultiInstance { get; set; } = true;

    public bool LowGraphics { get; set; } = false;

    public bool TileWindows { get; set; } = false;

    public bool ConfirmBatchLaunch { get; set; } = true;

    public string AccentName { get; set; } = "Ember";

    public List<RecentPlace> RecentPlaces { get; set; } = new();

    public int ServerPageSize { get; set; } = 100;

    public Dictionary<string, string> FastFlags { get; set; } = new();

    public int WindowWidth { get; set; } = 1200;

    public int WindowHeight { get; set; } = 760;

    public bool WindowMaximized { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt settings file must never stop the app from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile,
            System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
