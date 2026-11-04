using System.Diagnostics;
using Jankscope.Core;

namespace Jankscope.App.Services;

/// <summary>
/// Opens static, user-clicked external HTTPS URLs in the user's default browser (spec §4.1, HC-13).
/// Never fetches or pre-queries; strictly validates schemes; only Branding.SponsorUrl and Branding.StoreUrl may be opened.
/// </summary>
public static class ExternalUrlOpener
{
    public static bool TryOpen(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Only explicitly configured static outbound URLs are permitted (HC-13)
        if (url != Branding.SponsorUrl && url != Branding.StoreUrl)
        {
            return false;
        }

#pragma warning disable RS0030 // Opens user-clicked external HTTPS sponsor/store URL in default browser (HC-13)
        var info = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        };
        Process.Start(info);
#pragma warning restore RS0030

        return true;
    }
}
