namespace WayfarerMobile.Core.Algorithms;

/// <summary>
/// Geographic math utilities for distance, bearing, and coordinate calculations.
/// </summary>
public static class GeoMath
{
    /// <summary>
    /// Earth's radius in meters.
    /// </summary>
    public const double EarthRadiusMeters = 6371000;

    /// <summary>
    /// Calculates the distance between two coordinates using the Haversine formula.
    /// </summary>
    /// <param name="lat1">Latitude of first point in degrees.</param>
    /// <param name="lon1">Longitude of first point in degrees.</param>
    /// <param name="lat2">Latitude of second point in degrees.</param>
    /// <param name="lon2">Longitude of second point in degrees.</param>
    /// <returns>Distance in meters.</returns>
    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double lat1Rad = ToRadians(lat1);
        double lat2Rad = ToRadians(lat2);
        double deltaLatRad = ToRadians(lat2 - lat1);
        double deltaLonRad = ToRadians(lon2 - lon1);

        double a = Math.Sin(deltaLatRad / 2) * Math.Sin(deltaLatRad / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLonRad / 2) * Math.Sin(deltaLonRad / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Calculates the initial bearing from one coordinate to another.
    /// </summary>
    /// <param name="lat1">Latitude of starting point in degrees.</param>
    /// <param name="lon1">Longitude of starting point in degrees.</param>
    /// <param name="lat2">Latitude of destination point in degrees.</param>
    /// <param name="lon2">Longitude of destination point in degrees.</param>
    /// <returns>Bearing in degrees (0-360).</returns>
    public static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double lat1Rad = ToRadians(lat1);
        double lat2Rad = ToRadians(lat2);
        double deltaLonRad = ToRadians(lon2 - lon1);

        double y = Math.Sin(deltaLonRad) * Math.Cos(lat2Rad);
        double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                   Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(deltaLonRad);

        double bearingRad = Math.Atan2(y, x);
        double bearingDeg = ToDegrees(bearingRad);

        // Normalize to 0-360
        return (bearingDeg + 360) % 360;
    }

    /// <summary>
    /// Calculates a destination point given a start point, bearing, and distance.
    /// </summary>
    /// <param name="lat">Latitude of starting point in degrees.</param>
    /// <param name="lon">Longitude of starting point in degrees.</param>
    /// <param name="bearingDegrees">Bearing in degrees.</param>
    /// <param name="distanceMeters">Distance in meters.</param>
    /// <returns>Tuple of (latitude, longitude) in degrees.</returns>
    public static (double Latitude, double Longitude) CalculateDestination(
        double lat, double lon, double bearingDegrees, double distanceMeters)
    {
        double lat1 = ToRadians(lat);
        double lon1 = ToRadians(lon);
        double bearing = ToRadians(bearingDegrees);
        double angularDistance = distanceMeters / EarthRadiusMeters;

        double lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(angularDistance) +
            Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearing));

        double lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(lat1),
            Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2));

        return (ToDegrees(lat2), ToDegrees(lon2));
    }

    /// <summary>
    /// Calculates the speed between two points given their timestamps.
    /// </summary>
    /// <param name="lat1">Latitude of first point.</param>
    /// <param name="lon1">Longitude of first point.</param>
    /// <param name="time1">Timestamp of first point.</param>
    /// <param name="lat2">Latitude of second point.</param>
    /// <param name="lon2">Longitude of second point.</param>
    /// <param name="time2">Timestamp of second point.</param>
    /// <returns>Speed in meters per second.</returns>
    public static double CalculateSpeed(
        double lat1, double lon1, DateTime time1,
        double lat2, double lon2, DateTime time2)
    {
        double distance = CalculateDistance(lat1, lon1, lat2, lon2);
        double seconds = (time2 - time1).TotalSeconds;

        if (seconds <= 0)
            return 0;

        return distance / seconds;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    /// <param name="degrees">Angle in degrees.</param>
    /// <returns>Angle in radians.</returns>
    public static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    /// <param name="radians">Angle in radians.</param>
    /// <returns>Angle in degrees.</returns>
    public static double ToDegrees(double radians)
    {
        return radians * 180 / Math.PI;
    }

    /// <summary>
    /// Normalizes a bearing to be between 0 and 360 degrees.
    /// </summary>
    /// <param name="bearing">Bearing in degrees.</param>
    /// <returns>Normalized bearing (0-360).</returns>
    public static double NormalizeBearing(double bearing)
    {
        bearing %= 360;
        if (bearing < 0)
            bearing += 360;
        return bearing;
    }

    /// <summary>
    /// Calculates the difference between two bearings.
    /// </summary>
    /// <param name="bearing1">First bearing in degrees.</param>
    /// <param name="bearing2">Second bearing in degrees.</param>
    /// <returns>Difference in degrees (-180 to 180).</returns>
    public static double BearingDifference(double bearing1, double bearing2)
    {
        double diff = bearing2 - bearing1;
        while (diff > 180) diff -= 360;
        while (diff < -180) diff += 360;
        return diff;
    }
}
