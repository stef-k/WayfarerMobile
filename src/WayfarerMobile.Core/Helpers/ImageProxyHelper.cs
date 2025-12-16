using System.Text.RegularExpressions;
using System.Web;

namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// Helper class for converting image URLs to/from backend proxy URLs.
/// This allows images from Google MyMaps and other external sources to load
/// in WebView with proper authentication through the backend proxy.
/// </summary>
public static class ImageProxyHelper
{
    /// <summary>
    /// Convert Google MyMaps image URLs to backend proxy URLs for WebView display.
    /// Also converts relative proxy URLs to absolute URLs.
    /// </summary>
    /// <param name="html">The HTML content containing image tags.</param>
    /// <param name="backendBaseUrl">The backend server base URL (without /api suffix).</param>
    /// <returns>HTML with image URLs converted to proxy URLs.</returns>
    public static string ConvertImagesToProxyUrls(string? html, string? backendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            return html;
        }

        // Remove /api/ suffix if present and ensure no trailing slash
        var baseUrl = backendBaseUrl
            .Replace("/api/", "")
            .Replace("/api", "")
            .TrimEnd('/');

        var result = html;

        // Step 1: Convert Google MyMaps URLs to proxy URLs
        var googleMapsRegex = new Regex(
            @"(<img[^>]*src\s*=\s*[""'])(https://mymaps\.usercontent\.google\.com/[^""']*)([""'][^>]*>)",
            RegexOptions.IgnoreCase);

        result = googleMapsRegex.Replace(result, match =>
        {
            var prefix = match.Groups[1].Value;  // <img src="
            var googleUrl = match.Groups[2].Value;  // https://mymaps.usercontent.google.com/...
            var suffix = match.Groups[3].Value;  // ">

            try
            {
                // URL encode the Google URL and create proxy URL
                var encodedUrl = HttpUtility.UrlEncode(googleUrl);
                var proxyUrl = $"{baseUrl}/Public/ProxyImage?url={encodedUrl}";
                return prefix + proxyUrl + suffix;
            }
            catch
            {
                return match.Value;
            }
        });

        // Step 2: Convert backend relative proxy URLs to absolute URLs
        // Example: /Public/ProxyImage?url=... → https://backend.com/Public/ProxyImage?url=...
        var relativeProxyRegex = new Regex(
            @"(<img[^>]*src\s*=\s*[""'])/Public/ProxyImage\?url=([^""']*)([""'][^>]*>)",
            RegexOptions.IgnoreCase);

        result = relativeProxyRegex.Replace(result, match =>
        {
            var prefix = match.Groups[1].Value;  // <img src="
            var encodedUrl = match.Groups[2].Value;  // URL-encoded original URL
            var suffix = match.Groups[3].Value;  // ">

            var absoluteUrl = $"{baseUrl}/Public/ProxyImage?url={encodedUrl}";
            return prefix + absoluteUrl + suffix;
        });

        return result;
    }

    /// <summary>
    /// Convert backend proxy URLs back to original Google MyMaps URLs for server storage.
    /// This reverses the ConvertImagesToProxyUrls transformation.
    /// </summary>
    /// <param name="html">The HTML content containing proxy image URLs.</param>
    /// <param name="backendBaseUrl">The backend server base URL (without /api suffix).</param>
    /// <returns>HTML with proxy URLs converted back to original URLs.</returns>
    public static string ConvertProxyUrlsBackToOriginal(string? html, string? backendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        // Defensively unescape JSON-style sequences so regex matches on actual <img ...>
        var unescapedHtml = html;
        try
        {
            if (html.Contains("\\u003C", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("\\u003E", StringComparison.OrdinalIgnoreCase))
            {
                unescapedHtml = Regex.Unescape(html);
            }
        }
        catch
        {
            // Continue with original if unescape fails
        }

        if (string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            return unescapedHtml;
        }

        // Remove /api/ suffix if present and ensure no trailing slash
        var baseUrl = backendBaseUrl
            .Replace("/api/", "")
            .Replace("/api", "")
            .TrimEnd('/');

        // Escape special regex characters in the URL
        var escapedBackendUrl = Regex.Escape(baseUrl);

        // Match image tags with proxy URLs: <img src="https://backend.com/Public/ProxyImage?url=ENCODED_URL">
        var proxyImageRegex = new Regex(
            @"(<img[^>]*src\s*=\s*[""'])" + escapedBackendUrl + @"/Public/ProxyImage\?url=([^""']*)([""'][^>]*>)",
            RegexOptions.IgnoreCase);

        var result = proxyImageRegex.Replace(unescapedHtml, match =>
        {
            var prefix = match.Groups[1].Value;  // <img src="
            var encodedUrl = match.Groups[2].Value;  // URL-encoded original URL
            var suffix = match.Groups[3].Value;  // ">

            try
            {
                // URL decode to get the original URL
                var decodedUrl = HttpUtility.UrlDecode(encodedUrl);
                return prefix + decodedUrl + suffix;
            }
            catch
            {
                return match.Value;
            }
        });

        return result;
    }
}
