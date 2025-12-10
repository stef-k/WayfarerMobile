using WayfarerMobile.Core.Navigation;

namespace WayfarerMobile.Helpers;

/// <summary>
/// Utility class for decoding Google Encoded Polyline format.
/// </summary>
public static class PolylineDecoder
{
    /// <summary>
    /// Decodes an encoded polyline string to a list of coordinate points.
    /// Uses the standard Google Polyline Algorithm with 1e5 precision.
    /// </summary>
    /// <param name="encoded">The encoded polyline string.</param>
    /// <returns>List of decoded route points.</returns>
    public static List<RoutePoint> Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return new List<RoutePoint>();

        var points = new List<RoutePoint>();
        int index = 0;
        int lat = 0;
        int lng = 0;

        while (index < encoded.Length)
        {
            // Decode latitude
            int shift = 0;
            int result = 0;
            int b;

            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index < encoded.Length);

            lat += (result & 1) != 0 ? ~(result >> 1) : result >> 1;

            // Decode longitude
            shift = 0;
            result = 0;

            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20 && index < encoded.Length);

            lng += (result & 1) != 0 ? ~(result >> 1) : result >> 1;

            points.Add(new RoutePoint
            {
                Latitude = lat / 1e5,
                Longitude = lng / 1e5
            });
        }

        return points;
    }

    /// <summary>
    /// Decodes an encoded polyline to coordinate tuples (lat, lon).
    /// </summary>
    /// <param name="encoded">The encoded polyline string.</param>
    /// <returns>List of (latitude, longitude) tuples.</returns>
    public static List<(double Latitude, double Longitude)> DecodeToTuples(string encoded)
    {
        return Decode(encoded)
            .Select(p => (p.Latitude, p.Longitude))
            .ToList();
    }
}
