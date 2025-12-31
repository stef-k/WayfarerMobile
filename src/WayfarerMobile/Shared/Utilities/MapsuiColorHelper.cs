using Color = Mapsui.Styles.Color;

namespace WayfarerMobile.Shared.Utilities;

/// <summary>
/// Helper methods for working with Mapsui colors.
/// </summary>
public static class MapsuiColorHelper
{
    /// <summary>
    /// Default blue color used for map indicators (Google Maps blue).
    /// </summary>
    public static Color DefaultBlue => Color.FromArgb(255, 66, 133, 244);

    /// <summary>
    /// Parses a hex color string to a Mapsui Color.
    /// </summary>
    /// <param name="hexColor">Hex color string (e.g., "#FF5722" or "FF5722").</param>
    /// <param name="defaultColor">Optional default color if parsing fails. Uses DefaultBlue if not specified.</param>
    /// <returns>Parsed Color, or default if parsing fails.</returns>
    public static Color ParseHexColor(string? hexColor, Color? defaultColor = null)
    {
        var fallback = defaultColor ?? DefaultBlue;

        if (string.IsNullOrEmpty(hexColor))
            return fallback;

        try
        {
            var hex = hexColor.TrimStart('#');

            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                var a = Convert.ToByte(hex.Substring(0, 2), 16);
                var r = Convert.ToByte(hex.Substring(2, 2), 16);
                var g = Convert.ToByte(hex.Substring(4, 2), 16);
                var b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsuiColorHelper] Failed to parse color '{hexColor}': {ex.Message}");
        }

        return fallback;
    }
}
