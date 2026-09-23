using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Deck.Shell.Notifications;

/// <summary>
/// Executable icons as data URIs, for showing apps in the mixer by their logo rather than a
/// truncated process name ("Elgato.WaveLink" does not fit in 48 pixels).
///
/// Extraction is cached per path — it hits the disk, and the mixer redraws every couple of
/// seconds.
/// </summary>
internal static class AppIconCache
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? DataUri(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        if (Cache.TryGetValue(executablePath, out string? cached)) return cached;

        string? uri = null;

        try
        {
            if (File.Exists(executablePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                {
                    using var bitmap = icon.ToBitmap();
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Png);
                    uri = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
                }
            }
        }
        catch
        {
            // Packaged apps and protected paths can refuse; the row falls back to its name.
        }

        // Negative results are cached too, so a failing path isn't retried every refresh.
        Cache[executablePath] = uri;
        return uri;
    }
}
