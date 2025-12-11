namespace WayfarerMobile.Tests.Infrastructure.TestData;

/// <summary>
/// Sample location data for testing.
/// </summary>
public static class SampleLocations
{
    /// <summary>
    /// London coordinates (Big Ben).
    /// </summary>
    public static LocationData London => new()
    {
        Latitude = 51.5007,
        Longitude = -0.1246,
        Accuracy = 10,
        Timestamp = DateTime.UtcNow,
        Provider = "gps"
    };

    /// <summary>
    /// New York coordinates (Times Square).
    /// </summary>
    public static LocationData NewYork => new()
    {
        Latitude = 40.7580,
        Longitude = -73.9855,
        Accuracy = 15,
        Timestamp = DateTime.UtcNow,
        Provider = "gps"
    };

    /// <summary>
    /// Paris coordinates (Eiffel Tower).
    /// </summary>
    public static LocationData Paris => new()
    {
        Latitude = 48.8584,
        Longitude = 2.2945,
        Accuracy = 8,
        Timestamp = DateTime.UtcNow,
        Provider = "gps"
    };

    /// <summary>
    /// Tokyo coordinates (Tokyo Tower).
    /// </summary>
    public static LocationData Tokyo => new()
    {
        Latitude = 35.6586,
        Longitude = 139.7454,
        Accuracy = 12,
        Timestamp = DateTime.UtcNow,
        Provider = "gps"
    };

    /// <summary>
    /// Creates a location at specified coordinates with current timestamp.
    /// </summary>
    public static LocationData At(double latitude, double longitude, double accuracy = 10)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Accuracy = accuracy,
            Timestamp = DateTime.UtcNow,
            Provider = "gps"
        };
    }

    /// <summary>
    /// Creates a location at specified coordinates with specific timestamp.
    /// </summary>
    public static LocationData At(double latitude, double longitude, DateTime timestamp, double accuracy = 10)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Accuracy = accuracy,
            Timestamp = timestamp,
            Provider = "gps"
        };
    }

    /// <summary>
    /// Creates a sequence of locations along a path.
    /// </summary>
    /// <param name="start">Starting location.</param>
    /// <param name="bearing">Direction in degrees.</param>
    /// <param name="distancePerPointMeters">Distance between points.</param>
    /// <param name="count">Number of points.</param>
    /// <param name="intervalSeconds">Time between points.</param>
    /// <returns>List of locations along the path.</returns>
    public static List<LocationData> CreatePath(
        LocationData start,
        double bearing,
        double distancePerPointMeters,
        int count,
        int intervalSeconds = 60)
    {
        var locations = new List<LocationData> { start };
        var currentLat = start.Latitude;
        var currentLon = start.Longitude;
        var currentTime = start.Timestamp;

        for (int i = 1; i < count; i++)
        {
            var (newLat, newLon) = GeoMath.CalculateDestination(
                currentLat, currentLon, bearing, distancePerPointMeters);

            currentLat = newLat;
            currentLon = newLon;
            currentTime = currentTime.AddSeconds(intervalSeconds);

            locations.Add(new LocationData
            {
                Latitude = newLat,
                Longitude = newLon,
                Accuracy = start.Accuracy,
                Timestamp = currentTime,
                Provider = "gps"
            });
        }

        return locations;
    }

    /// <summary>
    /// Creates a random walk from a starting point.
    /// </summary>
    public static List<LocationData> CreateRandomWalk(
        LocationData start,
        int count,
        double maxStepMeters = 100,
        int intervalSeconds = 60)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var locations = new List<LocationData> { start };
        var currentLat = start.Latitude;
        var currentLon = start.Longitude;
        var currentTime = start.Timestamp;

        for (int i = 1; i < count; i++)
        {
            var bearing = random.NextDouble() * 360;
            var distance = random.NextDouble() * maxStepMeters;

            var (newLat, newLon) = GeoMath.CalculateDestination(
                currentLat, currentLon, bearing, distance);

            currentLat = newLat;
            currentLon = newLon;
            currentTime = currentTime.AddSeconds(intervalSeconds);

            locations.Add(new LocationData
            {
                Latitude = newLat,
                Longitude = newLon,
                Accuracy = 10 + random.NextDouble() * 20,
                Timestamp = currentTime,
                Provider = "gps"
            });
        }

        return locations;
    }
}
