using System.Text.Json;
using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Helpers;

/// <summary>
/// Helper class for preparing notes HTML content for display in WebView.
/// Uses the notes-viewer.html template for proper rendering with CSP and image handling.
/// </summary>
public static class NotesViewerHelper
{
    private static string? _cachedTemplate;
    private static bool _templateLoadAttempted;

    /// <summary>
    /// Gets whether the template has been loaded and is ready for use.
    /// </summary>
    public static bool IsTemplateLoaded => _cachedTemplate != null;

    /// <summary>
    /// Pre-loads the notes-viewer.html template. Call this during app initialization.
    /// </summary>
    public static async Task PreloadTemplateAsync()
    {
        if (_cachedTemplate != null || _templateLoadAttempted)
            return;

        _templateLoadAttempted = true;

        try
        {
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync("viewer/notes-viewer.html");
            using var reader = new StreamReader(stream);
            _cachedTemplate = await reader.ReadToEndAsync();
            System.Diagnostics.Debug.WriteLine("[NotesViewerHelper] Template loaded successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] Failed to load template: {ex.Message}");
        }
    }

    /// <summary>
    /// Prepares notes HTML for WebView display using the notes-viewer.html template.
    /// This is a synchronous version that requires PreloadTemplateAsync to be called first.
    /// </summary>
    /// <param name="notesHtml">The raw notes HTML content.</param>
    /// <param name="backendBaseUrl">The backend server base URL for image proxy.</param>
    /// <param name="isDarkMode">Whether to use dark mode styling.</param>
    /// <returns>Complete HTML ready for WebView, or null if notes are empty or template not loaded.</returns>
    public static HtmlWebViewSource? PrepareNotesHtml(
        string? notesHtml,
        string? backendBaseUrl,
        bool isDarkMode = false)
    {
        if (string.IsNullOrWhiteSpace(notesHtml))
            return null;

        if (_cachedTemplate == null)
        {
            System.Diagnostics.Debug.WriteLine("[NotesViewerHelper] Template not loaded, cannot prepare notes");
            return null;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] Input HTML (first 500): {(notesHtml.Length > 500 ? notesHtml[..500] : notesHtml)}");
            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] Backend URL: {backendBaseUrl}");

            // Convert images to proxy URLs
            var processedHtml = ImageProxyHelper.ConvertImagesToProxyUrls(notesHtml, backendBaseUrl);

            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] After proxy (first 500): {(processedHtml.Length > 500 ? processedHtml[..500] : processedHtml)}");

            // Remove plain text URLs that are already displayed as images
            // Google MyMaps often includes both <img> tags and the raw URL as text
            processedHtml = RemoveDuplicateImageUrls(processedHtml);

            // Auto-link plain URLs
            processedHtml = AutoLinkUrls(processedHtml);

            // JSON-serialize the content for safe injection into JavaScript
            var jsonContent = JsonSerializer.Serialize(processedHtml);

            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] JSON content (first 500): {(jsonContent.Length > 500 ? jsonContent[..500] : jsonContent)}");

            // Replace placeholder in template
            var finalHtml = _cachedTemplate
                .Replace("__WF_INITIAL_CONTENT__", jsonContent);

            return new HtmlWebViewSource { Html = finalHtml };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesViewerHelper] Error preparing notes: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prepares notes HTML for WebView display using the notes-viewer.html template (async version).
    /// </summary>
    /// <param name="notesHtml">The raw notes HTML content.</param>
    /// <param name="backendBaseUrl">The backend server base URL for image proxy.</param>
    /// <param name="isDarkMode">Whether to use dark mode styling.</param>
    /// <returns>Complete HTML ready for WebView, or null if template loading fails.</returns>
    public static async Task<HtmlWebViewSource?> PrepareNotesHtmlAsync(
        string? notesHtml,
        string? backendBaseUrl,
        bool isDarkMode = false)
    {
        if (string.IsNullOrWhiteSpace(notesHtml))
            return null;

        // Ensure template is loaded
        await PreloadTemplateAsync();

        return PrepareNotesHtml(notesHtml, backendBaseUrl, isDarkMode);
    }

    /// <summary>
    /// Removes plain text URLs that are already displayed as images.
    /// Google MyMaps often includes both the image tag and the raw URL as visible text.
    /// </summary>
    private static string RemoveDuplicateImageUrls(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Extract all image source URLs (both original and proxied)
        var imgSrcPattern = new System.Text.RegularExpressions.Regex(
            @"<img[^>]*src\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var imageSrcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in imgSrcPattern.Matches(html))
        {
            var src = match.Groups[1].Value;
            imageSrcs.Add(src);

            // If it's a proxy URL, also extract and add the original URL
            if (src.Contains("/Public/ProxyImage?url="))
            {
                var urlParamIndex = src.IndexOf("url=", StringComparison.OrdinalIgnoreCase);
                if (urlParamIndex >= 0)
                {
                    var encodedUrl = src[(urlParamIndex + 4)..];
                    try
                    {
                        var originalUrl = System.Web.HttpUtility.UrlDecode(encodedUrl);
                        imageSrcs.Add(originalUrl);
                    }
                    catch { }
                }
            }
        }

        if (imageSrcs.Count == 0)
            return html;

        var result = html;

        // Remove plain text occurrences of these URLs (not inside tags)
        foreach (var imageUrl in imageSrcs)
        {
            // Skip very short URLs to avoid false matches
            if (imageUrl.Length < 20)
                continue;

            // Escape special regex characters in the URL
            var escapedUrl = System.Text.RegularExpressions.Regex.Escape(imageUrl);

            // Match the URL when it appears as plain text (not inside an attribute)
            // Look for the URL followed by whitespace, < (tag start), or end of string
            var plainTextUrlPattern = new System.Text.RegularExpressions.Regex(
                $@"(?<![""'=]){escapedUrl}(?=\s|<|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            result = plainTextUrlPattern.Replace(result, "");
        }

        // Clean up any empty paragraphs or extra whitespace left behind
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<p>\s*</p>", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n\s*\n\s*\n", "\n\n");

        return result;
    }

    /// <summary>
    /// Auto-links plain URLs in HTML content that are not already inside HTML attributes.
    /// </summary>
    private static string AutoLinkUrls(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        // Regex to match URLs that are NOT inside HTML attributes
        // Negative lookbehind excludes URLs preceded by =" or =' (any attribute value)
        var urlPattern = new System.Text.RegularExpressions.Regex(
            @"(?<![=][""'])(https?://[^\s<>""']+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return urlPattern.Replace(html, match =>
        {
            var url = match.Value;
            // Clean up trailing punctuation that's likely not part of the URL
            var trailingPunctuation = "";
            while (url.Length > 0 && (url.EndsWith(".") || url.EndsWith(",") || url.EndsWith(")") || url.EndsWith(";")))
            {
                trailingPunctuation = url[^1] + trailingPunctuation;
                url = url[..^1];
            }
            return $"<a href=\"{url}\">{url}</a>{trailingPunctuation}";
        });
    }
}
