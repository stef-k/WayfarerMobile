namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// Catalog of available marker icon names and color variants.
/// Icons are sourced from Resources/Raw/wayfarer-map-icons.
/// </summary>
public static class IconCatalog
{
    /// <summary>
    /// Available marker color variants (directory names under dist/png/marker).
    /// </summary>
    public static readonly string[] MarkerColors =
    [
        "bg-black",
        "bg-blue",
        "bg-green",
        "bg-purple",
        "bg-red"
    ];

    /// <summary>
    /// Available marker icon names (file basenames).
    /// </summary>
    public static readonly string[] IconNames =
    [
        "anchor", "atm", "barbecue", "beach", "bike", "boat", "camera", "camping",
        "car", "charging-point", "checkmark", "clouds", "construction", "danger",
        "drink", "eat", "ev-station", "fitness", "flag", "flight", "gas", "help",
        "hike", "hospital", "hotel", "info", "kayak", "latest", "luggage", "map",
        "marker", "museum", "no-wheelchair", "no-wifi", "park", "parking", "pet",
        "pharmacy", "phishing", "police", "run", "sail", "scuba-dive", "sea",
        "shopping", "ski", "smoke-free", "smoke", "sos", "star", "subway", "surf",
        "swim", "taxi", "telephone", "thunderstorm", "tool", "train", "walk",
        "water", "wc", "wheelchair", "wifi"
    ];

    /// <summary>
    /// Commonly used icons that should be promoted in pickers.
    /// </summary>
    public static readonly string[] PriorityIconNames =
    [
        "marker", "star", "camera", "museum", "eat", "drink", "hotel",
        "info", "help", "flag", "danger", "beach", "hike", "wc", "sos", "map"
    ];

    /// <summary>
    /// Validates or coerces a color to a known variant.
    /// </summary>
    /// <param name="color">The color to validate.</param>
    /// <param name="fallback">Fallback color if invalid.</param>
    /// <returns>A valid color string.</returns>
    public static string CoerceColor(string? color, string fallback = "bg-blue")
    {
        if (string.IsNullOrWhiteSpace(color))
            return fallback;

        var normalized = color.StartsWith("bg-") ? color : $"bg-{color}";
        return MarkerColors.Contains(normalized) ? normalized : fallback;
    }

    /// <summary>
    /// Validates or coerces an icon name to a known icon.
    /// </summary>
    /// <param name="iconName">The icon name to validate.</param>
    /// <param name="fallback">Fallback icon if invalid.</param>
    /// <returns>A valid icon name.</returns>
    public static string CoerceIcon(string? iconName, string fallback = "marker")
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return fallback;

        return IconNames.Contains(iconName) ? iconName : fallback;
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
    /// Converts a marker color to its hex equivalent.
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
            _ => "#2196F3"
        };
    }
}
