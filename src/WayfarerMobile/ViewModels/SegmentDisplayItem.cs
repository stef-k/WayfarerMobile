namespace WayfarerMobile.ViewModels;

/// <summary>
/// Display model for trip segments in the sidebar.
/// </summary>
public class SegmentDisplayItem
{
    /// <summary>
    /// Gets or sets the segment ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the origin place name.
    /// </summary>
    public string OriginName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination place name.
    /// </summary>
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport mode.
    /// </summary>
    public string TransportMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the distance in kilometers.
    /// </summary>
    public double? DistanceKm { get; set; }

    /// <summary>
    /// Gets or sets the duration in minutes.
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Gets the description text (Origin → Destination).
    /// </summary>
    public string Description => $"{OriginName} → {DestinationName}";

    /// <summary>
    /// Gets the distance and duration text.
    /// </summary>
    public string DistanceAndDuration
    {
        get
        {
            var parts = new List<string>();

            if (DistanceKm.HasValue)
            {
                parts.Add(DistanceKm.Value >= 1
                    ? $"{DistanceKm.Value:F1} km"
                    : $"{DistanceKm.Value * 1000:F0} m");
            }

            if (DurationMinutes.HasValue)
            {
                if (DurationMinutes.Value >= 60)
                {
                    var hours = DurationMinutes.Value / 60;
                    var mins = DurationMinutes.Value % 60;
                    parts.Add(mins > 0 ? $"{hours}h {mins}m" : $"{hours}h");
                }
                else
                {
                    parts.Add($"{DurationMinutes.Value} min");
                }
            }

            return parts.Count > 0 ? string.Join(" • ", parts) : "—";
        }
    }

    /// <summary>
    /// Gets the transport mode icon.
    /// </summary>
    public string TransportIcon => TransportMode?.ToLowerInvariant() switch
    {
        "walk" or "walking" => "🚶",
        "drive" or "driving" or "car" => "🚗",
        "transit" or "bus" => "🚌",
        "train" or "rail" => "🚆",
        "subway" or "metro" => "🚇",
        "bike" or "bicycle" or "cycling" => "🚴",
        "ferry" or "boat" => "⛴️",
        "taxi" or "rideshare" => "🚕",
        "flight" or "plane" => "✈️",
        _ => "📍"
    };
}
