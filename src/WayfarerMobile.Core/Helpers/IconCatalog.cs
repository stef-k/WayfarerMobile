namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// Catalog of marker icon utilities.
/// Icons are dynamically loaded from Resources/Raw/wayfarer-map-icons.
/// No hardcoded icon list - any icon name is accepted and loaded on-demand.
/// If loading fails, TripLayerService falls back to a colored ellipse marker.
/// </summary>
public static class IconCatalog
{
    /// <summary>
    /// Default marker color when none specified.
    /// </summary>
    public const string DefaultColor = "bg-blue";

    /// <summary>
    /// Default marker icon when none specified.
    /// </summary>
    public const string DefaultIcon = "marker";

    /// <summary>
    /// Commonly used icons that should be promoted in pickers.
    /// Use TripLayerService.GetValidatedPriorityIconsAsync to filter to existing icons.
    /// </summary>
    public static readonly string[] PriorityIconNames =
    [
        "marker", "star", "camera", "museum", "eat", "drink", "hotel",
        "info", "help", "flag", "danger", "beach", "hike", "wc", "sos", "map"
    ];

    /// <summary>
    /// Normalizes a color string, returning fallback for null/empty.
    /// Does not validate - any color name is accepted for dynamic loading.
    /// </summary>
    /// <param name="color">The color to normalize.</param>
    /// <param name="fallback">Fallback color if null/empty.</param>
    /// <returns>Normalized color string with "bg-" prefix.</returns>
    public static string CoerceColor(string? color, string fallback = DefaultColor)
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        // Ensure "bg-" prefix for consistency
        return color.StartsWith("bg-") ? color : $"bg-{color}";
    }

    /// <summary>
    /// Normalizes an icon name, returning fallback for null/empty.
    /// Does not validate - any icon name is accepted for dynamic loading.
    /// </summary>
    /// <param name="iconName">The icon name to normalize.</param>
    /// <param name="fallback">Fallback icon if null/empty.</param>
    /// <returns>The icon name or fallback.</returns>
    public static string CoerceIcon(string? iconName, string fallback = DefaultIcon)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return fallback;

        return iconName;
    }

    /// <summary>
    /// Gets the resource path for a marker icon.
    /// </summary>
    /// <param name="iconName">Icon name (e.g., "marker", "star").</param>
    /// <param name="color">Color variant (e.g., "bg-blue", "bg-red").</param>
    /// <returns>The resource path for the icon PNG.</returns>
    public static string GetIconResourcePath(string? iconName, string? color)
    {
        var validIcon = CoerceIcon(iconName);
        var validColor = CoerceColor(color);
        return $"wayfarer-map-icons/dist/png/marker/{validColor}/{validIcon}.png";
    }

    /// <summary>
    /// Converts a marker color to its hex equivalent for fallback rendering.
    /// Known colors return their mapped hex value; unknown colors return default blue.
    /// </summary>
    /// <param name="markerColor">The marker color (e.g., "bg-blue").</param>
    /// <returns>Hex color string.</returns>
    public static string GetHexColor(string? markerColor)
    {
        var color = CoerceColor(markerColor);
        return color switch
        {
            "bg-black" => "#212121",
            "bg-blue" => "#2196F3",
            "bg-green" => "#4CAF50",
            "bg-purple" => "#9C27B0",
            "bg-red" => "#F44336",
            _ => "#2196F3" // Default to blue for unknown colors
        };
    }
}
