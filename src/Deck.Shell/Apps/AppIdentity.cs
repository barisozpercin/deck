using System.IO;
using System.Xml.Linq;
using Deck.Shell.Notifications;

namespace Deck.Shell.Apps;

internal sealed record AppIdentity(string? DisplayName, string? IconDataUri);

/// <summary>
/// Works out what an application is actually called and what it looks like.
///
/// Process names and file metadata are unreliable on their own: WhatsApp's audio comes from
/// <c>WhatsApp.Root.exe</c>, whose FileDescription is literally "WhatsApp.Root" and whose
/// embedded icon is a blank document. For packaged (MSIX) apps the truth lives in the package
/// manifest, which says DisplayName "WhatsApp" and points at a real logo asset.
/// </summary>
internal static class AppIdentityResolver
{
    private static readonly Dictionary<string, AppIdentity> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Preferred logo variants, best first. "unplated" has no coloured tile behind it.</summary>
    private static readonly string[] LogoSuffixes =
    [
        ".targetsize-32_altform-unplated.png",
        ".targetsize-32.png",
        ".targetsize-24_altform-unplated.png",
        ".targetsize-24.png",
        ".scale-200.png",
        ".targetsize-20_altform-unplated.png",
        ".scale-100.png",
        ".png"
    ];

    public static AppIdentity Resolve(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return new AppIdentity(null, null);
        if (Cache.TryGetValue(executablePath, out var cached)) return cached;

        string? name = null;
        string? icon = null;

        if (FindPackageRoot(executablePath) is { } packageRoot)
        {
            (name, icon) = FromManifest(packageRoot);
        }

        name ??= FromVersionInfo(executablePath);
        icon ??= AppIconCache.DataUri(executablePath);

        var identity = new AppIdentity(name, icon);
        Cache[executablePath] = identity;
        return identity;
    }

    private static readonly HashSet<string> GenericAppIds =
        new(StringComparer.OrdinalIgnoreCase) { "App", "Application", "App1", "Default" };

    private static readonly Dictionary<string, string?> AumidCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A readable name for a media session's app id, e.g.
    /// "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" → "Spotify".
    ///
    /// The package folder can't simply be looked up: enumerating C:\Program Files\WindowsApps is
    /// denied to normal users, so we can only read a package we already know the path of.
    /// </summary>
    public static string? FriendlyAppName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return null;
        if (AumidCache.TryGetValue(appUserModelId, out string? cached)) return cached;

        string? result = ResolveAumid(appUserModelId);
        AumidCache[appUserModelId] = result;
        return result;
    }

    private static string? ResolveAumid(string appUserModelId)
    {
        string[] parts = appUserModelId.Split('!');

        // Packaged apps usually carry a meaningful application id after the '!'. Spotify's is
        // literally "Spotify"; WhatsApp's is the useless "App".
        if (parts.Length > 1 && parts[1].Length > 1 && !GenericAppIds.Contains(parts[1]))
            return parts[1];

        string family = parts[0];
        int underscore = family.LastIndexOf('_');
        string packageName = underscore > 0 ? family[..underscore] : family;

        // Fall back to finding a live process inside that package and reading its manifest.
        string marker = $@"\WindowsApps\{packageName}_";

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string path = Interop.WindowNative.GetProcessPath((uint)process.Id);
                if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return Resolve(path).DisplayName;
            }
            catch
            {
                // Process vanished or is unreadable.
            }
            finally
            {
                process.Dispose();
            }
        }

        // Last resort: strip the publisher prefix — "5319275A.WhatsAppDesktop" → "WhatsAppDesktop".
        int dot = packageName.LastIndexOf('.');
        return dot > 0 && dot < packageName.Length - 1 ? packageName[(dot + 1)..] : packageName;
    }

    /// <summary>The nearest ancestor directory holding an AppxManifest.xml, if any.</summary>
    private static string? FindPackageRoot(string executablePath)
    {
        try
        {
            var directory = new FileInfo(executablePath).Directory;

            for (int depth = 0; depth < 3 && directory is not null; depth++)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AppxManifest.xml")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }
        catch
        {
            // Unreadable path; fall through to the non-packaged route.
        }

        return null;
    }

    private static (string? Name, string? Icon) FromManifest(string packageRoot)
    {
        try
        {
            var document = XDocument.Load(Path.Combine(packageRoot, "AppxManifest.xml"));

            // Matching on local names sidesteps the several manifest namespace versions.
            var visualElements = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "VisualElements");

            string? name = Clean(visualElements?.Attribute("DisplayName")?.Value)
                ?? Clean(document.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "DisplayName" && e.Parent?.Name.LocalName == "Properties")?.Value);

            string? icon = null;
            string? logo = visualElements?.Attribute("Square44x44Logo")?.Value;
            if (!string.IsNullOrWhiteSpace(logo)) icon = ReadLogo(packageRoot, logo);

            return (name, icon);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>ms-resource references need the resource loader; treat those as unknown.</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static string? ReadLogo(string packageRoot, string relativeLogo)
    {
        try
        {
            string directory = Path.Combine(packageRoot, Path.GetDirectoryName(relativeLogo) ?? "");
            if (!Directory.Exists(directory)) return null;

            string baseName = Path.GetFileNameWithoutExtension(relativeLogo);

            // Manifests name a logo that often doesn't exist on disk — Windows picks a scale or
            // target-size variant instead. Try the useful ones in order.
            foreach (string suffix in LogoSuffixes)
            {
                string candidate = Path.Combine(directory, baseName + suffix);
                if (File.Exists(candidate)) return ToDataUri(candidate);
            }

            string? any = Directory.EnumerateFiles(directory, baseName + "*.png").FirstOrDefault();
            return any is null ? null : ToDataUri(any);
        }
        catch
        {
            return null;
        }
    }

    private static string? ToDataUri(string pngPath)
    {
        try
        {
            var info = new FileInfo(pngPath);
            if (info.Length > 256 * 1024) return null;   // an icon has no business being this big

            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(pngPath));
        }
        catch
        {
            return null;
        }
    }

    private static string? FromVersionInfo(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath)) return null;

            string? description = System.Diagnostics.FileVersionInfo
                .GetVersionInfo(executablePath).FileDescription?.Trim();

            if (string.IsNullOrWhiteSpace(description)) return null;

            // Some apps just repeat the filename ("WhatsApp.Root"), which is no better than
            // the process name we already have.
            string fileName = Path.GetFileNameWithoutExtension(executablePath);
            return string.Equals(description, fileName, StringComparison.OrdinalIgnoreCase)
                ? null
                : description;
        }
        catch
        {
            return null;
        }
    }
}
