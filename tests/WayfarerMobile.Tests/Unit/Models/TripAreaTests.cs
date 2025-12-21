namespace WayfarerMobile.Tests.Unit.Models;

public class TripAreaTests
{
    [Fact]
    public void Center_WithTriangleBoundary_ReturnsAverageCoordinates()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "Triangle",
            Boundary = new List<GeoCoordinate>
            {
                new() { Latitude = 51.0, Longitude = -0.1 },
                new() { Latitude = 51.0, Longitude = 0.1 },
                new() { Latitude = 52.0, Longitude = 0.0 }
            }
        };

        var center = area.Center;

        center.Should().NotBeNull();
        center!.Latitude.Should().BeApproximately(51.333, 0.01);
        center.Longitude.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void Center_WithSquareBoundary_ReturnsCenterPoint()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "Square",
            Boundary = new List<GeoCoordinate>
            {
                new() { Latitude = 50.0, Longitude = 0.0 },
                new() { Latitude = 50.0, Longitude = 2.0 },
                new() { Latitude = 52.0, Longitude = 2.0 },
                new() { Latitude = 52.0, Longitude = 0.0 }
            }
        };

        var center = area.Center;

        center.Should().NotBeNull();
        center!.Latitude.Should().BeApproximately(51.0, 0.001);
        center.Longitude.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Center_WithEmptyBoundary_ReturnsNull()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "Empty",
            Boundary = new List<GeoCoordinate>()
        };

        var center = area.Center;

        center.Should().BeNull();
    }

    [Fact]
    public void Center_WithDefaultBoundary_ReturnsNull()
    {
        var area = new TripArea { Id = Guid.NewGuid(), Name = "NoSet" };

        var center = area.Center;

        center.Should().BeNull();
    }

    [Fact]
    public void Center_WithSinglePoint_ReturnsThatPoint()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "Single",
            Boundary = new List<GeoCoordinate>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 }
            }
        };

        var center = area.Center;

        center.Should().NotBeNull();
        center!.Latitude.Should().Be(51.5074);
        center.Longitude.Should().Be(-0.1278);
    }

    [Fact]
    public void Center_NegativeCoordinates_CalculatesCorrectly()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "South",
            Boundary = new List<GeoCoordinate>
            {
                new() { Latitude = -20.0, Longitude = -60.0 },
                new() { Latitude = -20.0, Longitude = -40.0 },
                new() { Latitude = -30.0, Longitude = -40.0 },
                new() { Latitude = -30.0, Longitude = -60.0 }
            }
        };

        var center = area.Center;

        center.Should().NotBeNull();
        center!.Latitude.Should().BeApproximately(-25.0, 0.001);
        center.Longitude.Should().BeApproximately(-50.0, 0.001);
    }

    [Fact]
    public void Center_CrossingEquator_CalculatesCorrectly()
    {
        var area = new TripArea
        {
            Id = Guid.NewGuid(),
            Name = "Equator",
            Boundary = new List<GeoCoordinate>
            {
                new() { Latitude = 5.0, Longitude = 10.0 },
                new() { Latitude = -5.0, Longitude = 10.0 },
                new() { Latitude = -5.0, Longitude = 20.0 },
                new() { Latitude = 5.0, Longitude = 20.0 }
            }
        };

        var center = area.Center;

        center.Should().NotBeNull();
        center!.Latitude.Should().BeApproximately(0.0, 0.001);
        center.Longitude.Should().BeApproximately(15.0, 0.001);
    }
}
