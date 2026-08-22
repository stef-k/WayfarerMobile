using System.Text.Json;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

public static class SegmentWaypointJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyCollection<TripSegmentWaypoint>? waypoints) =>
        JsonSerializer.Serialize(waypoints ?? Array.Empty<TripSegmentWaypoint>(), Options);

    public static List<TripSegmentWaypoint> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<TripSegmentWaypoint>>(json, Options) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
