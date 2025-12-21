using System.Globalization;
using System.Text.RegularExpressions;

namespace WayfarerMobile.Converters;

/// <summary>
/// Converts HTML content to plain text by stripping tags and decoding entities.
/// </summary>
public partial class HtmlToTextConverter : IValueConverter
{
    /// <summary>
    /// Converts HTML string to plain text.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string html || string.IsNullOrWhiteSpace(html))
            return value;

        return StripHtml(html);
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Strips HTML tags and decodes common entities.
    /// </summary>
    private static string StripHtml(string html)
    {
        // Replace common block elements with newlines
        var text = BlockTagsRegex().Replace(html, "\n");

        // Replace <br> tags with newlines
        text = BrTagsRegex().Replace(text, "\n");

        // Remove all remaining HTML tags
        text = AllTagsRegex().Replace(text, string.Empty);

        // Decode common HTML entities
        text = DecodeHtmlEntities(text);

        // Clean up excessive whitespace
        text = ExcessiveNewlinesRegex().Replace(text, "\n\n");
        text = text.Trim();

        return text;
    }

    /// <summary>
    /// Decodes common HTML entities.
    /// </summary>
    private static string DecodeHtmlEntities(string text)
    {
        // Common HTML entities
        text = text.Replace("&nbsp;", " ");
        text = text.Replace("&amp;", "&");
        text = text.Replace("&lt;", "<");
        text = text.Replace("&gt;", ">");
        text = text.Replace("&quot;", "\"");
        text = text.Replace("&apos;", "'");
        text = text.Replace("&#39;", "'");
        text = text.Replace("&ndash;", "–");
        text = text.Replace("&mdash;", "—");
        text = text.Replace("&hellip;", "...");
        text = text.Replace("&copy;", "©");
        text = text.Replace("&reg;", "®");
        text = text.Replace("&trade;", "™");

        // Decode numeric entities
        text = NumericEntityRegex().Replace(text, m =>
        {
            if (int.TryParse(m.Groups[1].Value, out var code))
                return ((char)code).ToString();
            return m.Value;
        });

        return text;
    }

    [GeneratedRegex(@"</(p|div|h[1-6]|li|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagsRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTagsRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AllTagsRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();

    [GeneratedRegex(@"&#(\d+);")]
    private static partial Regex NumericEntityRegex();
}
