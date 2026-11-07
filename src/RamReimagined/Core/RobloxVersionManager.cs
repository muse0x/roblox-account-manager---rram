using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RamReimagined.Core;

public sealed record RobloxDeployment(string VersionHash, string VersionNumber, DateTimeOffset? PublishedAt);

public sealed record InstalledRobloxVersion(string Folder, string ExecutablePath, string FileVersion, DateTime LastWriteUtc)
{
    public string Display => string.IsNullOrWhiteSpace(FileVersion)
        ? Folder
        : $"v{FileVersion}  |  {Folder}";
}

public sealed record VersionInstallProgress(string Message, double Fraction, long DownloadedBytes = 0, long TotalBytes = 0);

/// <summary>
/// Installs a specific Roblox WindowsPlayer deployment into Versions\version-.
/// The package layout follows RDD: setup-aws.rbxcdn.com serves the manifest and
/// package zips, which are unpacked into the same structure the client expects.
/// </summary>
public sealed class RobloxVersionManager
{
    private const string SetupBase = "https://setup-aws.rbxcdn.com";
    private const string WeaoBase = "https://weao.gg";

    private const string AppSettingsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<Settings>\n    <ContentFolder>content</ContentFolder>\n    <BaseUrl>http://www.roblox.com</BaseUrl>\n</Settings>\n";

    private static readonly Dictionary<string, string> PlayerExtractRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["redist.zip"] = "",
        ["shaders.zip"] = "shaders/",
        ["ssl.zip"] = "ssl/",
        ["WebView2.zip"] = "",
        ["WebView2RuntimeInstaller.zip"] = "WebView2RuntimeInstaller/",
        ["content-avatar.zip"] = "content/avatar/",
        ["content-configs.zip"] = "content/configs/",
        ["content-fonts.zip"] = "content/fonts/",
        ["content-sky.zip"] = "content/sky/",
        ["content-sounds.zip"] = "content/sounds/",
        ["content-textures2.zip"] = "content/textures/",
        ["content-models.zip"] = "content/models/",
        ["content-platform-fonts.zip"] = "PlatformContent/pc/fonts/",
        ["content-platform-dictionaries.zip"] = "PlatformContent/pc/shared_compression_dictionaries/",
        ["content-terrain.zip"] = "PlatformContent/pc/terrain/",
        ["content-textures3.zip"] = "PlatformContent/pc/textures/",
        ["extracontent-luapackages.zip"] = "ExtraContent/LuaPackages/",
        ["extracontent-translations.zip"] = "ExtraContent/translations/",
        ["extracontent-models.zip"] = "ExtraContent/models/",
        ["extracontent-textures.zip"] = "ExtraContent/textures/",
        ["extracontent-places.zip"] = "ExtraContent/places/"
    };

    private static readonly HttpClient Http = CreateHttpClient();

    public static string VersionsDirectory =>
        Path.Combine(AppPaths.LocalRobloxDirectory, "Versions");

    public static string? NormalizeVersion(string? RawVersion)
    {
        var Value = (RawVersion ?? "").Trim().ToLowerInvariant();
        if (Value.Length == 0)
        {
            return null;
        }

        if (!Value.StartsWith("version-", StringComparison.Ordinal))
        {
            Value = "version-" + Value;
        }

        return Regex.IsMatch(Value, "^version-[0-9a-f]{8,32}$") ? Value : null;
    }

    public async Task<RobloxDeployment> GetLatestAsync(bool Previous, CancellationToken Ct = default)
    {
        var Url = $"{WeaoBase}/api/versions/{(Previous ? "past" : "current")}";
        using var Response = await Http.GetAsync(Url, Ct).ConfigureAwait(false);
        Response.EnsureSuccessStatusCode();
        using var Document = JsonDocument.Parse(await Response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        var Root = Document.RootElement;

        var VersionHash = Root.TryGetProperty("Windows", out var HashElement) ? HashElement.GetString() ?? "" : "";
        var VersionNumber = "";
        if (Root.TryGetProperty("WindowsResponse", out var ResponseElement) &&
            ResponseElement.TryGetProperty("version", out var VersionElement))
        {
            VersionNumber = VersionElement.GetString() ?? "";
        }

        DateTimeOffset? PublishedAt = null;
        if (Root.TryGetProperty("WindowsDate", out var DateElement) &&
            DateTimeOffset.TryParse(DateElement.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var ParsedDate))
        {
            PublishedAt = ParsedDate;
        }

        if (VersionHash.Length == 0)
        {
            throw new InvalidOperationException("WEAO returned no Windows deployment.");
        }

        return new RobloxDeployment(VersionHash, VersionNumber, PublishedAt);
    }

    public IReadOnlyList<InstalledRobloxVersion> GetInstalled()
    {
        var Result = new List<InstalledRobloxVersion>();
        if (!Directory.Exists(VersionsDirectory))
        {
            return Result;
        }

        try
        {
            foreach (var DirectoryPath in Directory.EnumerateDirectories(VersionsDirectory))
            {
                var ExecutablePath = Path.Combine(DirectoryPath, "RobloxPlayerBeta.exe");
                if (!File.Exists(ExecutablePath))
                {
                    continue;
                }

                var FileVersion = "";
                try
                {
                    FileVersion = NormalizeFileVersion(FileVersionInfo.GetVersionInfo(ExecutablePath).FileVersion);
                }
                catch (Exception)
                {
                    // FileVersion is cosmetic; the folder is still launchable.
                }

                Result.Add(new InstalledRobloxVersion(Path.GetFileName(DirectoryPath), ExecutablePath,
                    FileVersion, Directory.GetLastWriteTimeUtc(DirectoryPath)));
            }
        }
        catch (Exception)
        {
            return Result;
        }

        return Result.OrderByDescending(Item => Item.LastWriteUtc).ToList();
    }

    public async Task<InstalledRobloxVersion> InstallAsync(string? RawVersion,
        IProgress<VersionInstallProgress>? Progress = null, CancellationToken Ct = default)
    {
        var Version = NormalizeVersion(RawVersion)
            ?? throw new ArgumentException("Enter a version hash such as version-f0ea5da9412f4e35.");

        Directory.CreateDirectory(VersionsDirectory);
        var FinalDirectory = Path.Combine(VersionsDirectory, Version);
        var FinalExecutable = Path.Combine(FinalDirectory, "RobloxPlayerBeta.exe");
        if (File.Exists(FinalExecutable))
        {
            return ToInstalled(Version, FinalExecutable);
        }

        if (Directory.Exists(FinalDirectory))
        {
            try
            {
                Directory.Delete(FinalDirectory, true);
            }
            catch (Exception Exception)
            {
                throw new IOException("Could not replace the incomplete version folder.", Exception);
            }
        }

        var TempDirectory = Path.Combine(VersionsDirectory, $".{Version}-{Guid.NewGuid():N}.install");
        Directory.CreateDirectory(TempDirectory);

        try
        {
            Progress?.Report(new VersionInstallProgress("Fetching package manifest", 0));
            var ManifestUrl = $"{SetupBase}/{Version}-rbxPkgManifest.txt";
            string Manifest;
            using (var Response = await Http.GetAsync(ManifestUrl, Ct).ConfigureAwait(false))
            {
                if (!Response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(Response.StatusCode == HttpStatusCode.NotFound
                        ? "This build is not on the Roblox CDN yet. Try again later or pick another version."
                        : $"Manifest request failed ({(int)Response.StatusCode}).");
                }

                Manifest = await Response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false);
            }

            var Lines = Manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Line => Line.Trim())
                .ToList();
            if (Lines.Count == 0 || Lines[0] != "v0")
            {
                throw new InvalidOperationException("Unexpected Roblox package manifest.");
            }

            var Packages = Lines
                .Where(Line => Line.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Line.Contains('.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!Packages.Contains("RobloxApp.zip", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This deployment is not a Windows player build.");
            }

            await File.WriteAllTextAsync(Path.Combine(TempDirectory, "AppSettings.xml"), AppSettingsXml, Ct)
                .ConfigureAwait(false);

            var Completed = 0;
            foreach (var Package in Packages)
            {
                Ct.ThrowIfCancellationRequested();
                var PackageUrl = $"{SetupBase}/{Version}-{Package}";
                var PackageTemp = Path.Combine(Path.GetTempPath(), $"ram-{Guid.NewGuid():N}-{Package}");

                try
                {
                    await DownloadAsync(PackageUrl, PackageTemp, (Downloaded, Total) =>
                    {
                        var Within = Total > 0 ? (double)Downloaded / Total : 0;
                        var Fraction = (Completed + Within) / Packages.Count;
                        Progress?.Report(new VersionInstallProgress(
                            $"Downloading {Package} ({Completed + 1}/{Packages.Count})",
                            Math.Clamp(Fraction, 0, 1), Downloaded, Total));
                    }, Ct).ConfigureAwait(false);

                    if (PlayerExtractRoots.TryGetValue(Package, out var ExtractRoot))
                    {
                        ExtractPackage(PackageTemp, TempDirectory, ExtractRoot);
                    }
                    else
                    {
                        File.Copy(PackageTemp, Path.Combine(TempDirectory, Package), true);
                    }
                }
                finally
                {
                    try
                    {
                        if (File.Exists(PackageTemp))
                        {
                            File.Delete(PackageTemp);
                        }
                    }
                    catch (Exception)
                    {
                        // A leftover temp package is harmless.
                    }
                }

                Completed++;
                Progress?.Report(new VersionInstallProgress(
                    $"Installed {Package} ({Completed}/{Packages.Count})",
                    (double)Completed / Packages.Count));
            }

            var TempExecutable = Path.Combine(TempDirectory, "RobloxPlayerBeta.exe");
            if (!File.Exists(TempExecutable))
            {
                throw new InvalidOperationException("The deployment finished without RobloxPlayerBeta.exe.");
            }

            Directory.Move(TempDirectory, FinalDirectory);
            return ToInstalled(Version, Path.Combine(FinalDirectory, "RobloxPlayerBeta.exe"));
        }
        catch
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                {
                    Directory.Delete(TempDirectory, true);
                }
            }
            catch (Exception)
            {
                // Best effort cleanup.
            }

            throw;
        }
    }

    private static async Task DownloadAsync(string Url, string DestinationPath,
        Action<long, long> Progress, CancellationToken Ct)
    {
        using var Response = await Http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, Ct)
            .ConfigureAwait(false);
        Response.EnsureSuccessStatusCode();

        var Total = Response.Content.Headers.ContentLength ?? 0;
        await using var Source = await Response.Content.ReadAsStreamAsync(Ct).ConfigureAwait(false);
        await using var Destination = new FileStream(DestinationPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        var Buffer = new byte[81920];
        long Downloaded = 0;
        long LastReport = 0;
        int Read;
        while ((Read = await Source.ReadAsync(Buffer, Ct).ConfigureAwait(false)) > 0)
        {
            await Destination.WriteAsync(Buffer.AsMemory(0, Read), Ct).ConfigureAwait(false);
            Downloaded += Read;

            var Now = Environment.TickCount64;
            if (Now - LastReport >= 150)
            {
                LastReport = Now;
                Progress(Downloaded, Total);
            }
        }

        Progress(Downloaded, Total);
    }

    private static void ExtractPackage(string ZipPath, string TargetRoot, string RelativeRoot)
    {
        using var Archive = ZipFile.OpenRead(ZipPath);
        var RootFull = Path.GetFullPath(TargetRoot);

        foreach (var Entry in Archive.Entries)
        {
            if (string.IsNullOrEmpty(Entry.Name))
            {
                continue;
            }

            var Relative = Entry.FullName.Replace('\\', '/').TrimStart('/');
            if (Relative.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            var Destination = Path.GetFullPath(Path.Combine(RootFull,
                RelativeRoot.Replace('/', '\\'), Relative.Replace('/', '\\')));
            if (!Destination.StartsWith(RootFull + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            Entry.ExtractToFile(Destination, true);
        }
    }

    private static string NormalizeFileVersion(string? Value) => string.IsNullOrWhiteSpace(Value) ? "" : Value.Replace(", ", ".").Replace(',', '.').Trim();

    private static InstalledRobloxVersion ToInstalled(string Folder, string ExecutablePath)
    {
        var FileVersion = "";
        try
        {
            FileVersion = NormalizeFileVersion(FileVersionInfo.GetVersionInfo(ExecutablePath).FileVersion);
        }
        catch (Exception)
        {
            // Cosmetic only.
        }

        return new InstalledRobloxVersion(Folder, ExecutablePath, FileVersion, DateTime.UtcNow);
    }

    private static HttpClient CreateHttpClient()
    {
        var Handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        var Client = new HttpClient(Handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return Client;
    }
}
