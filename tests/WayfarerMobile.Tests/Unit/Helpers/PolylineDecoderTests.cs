namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for PolylineDecoder class.
/// Tests Google Encoded Polyline format decoding.
/// </summary>
public class PolylineDecoderTests
{
    #region Decode Tests

    [Fact]
    public void Decode_EmptyString_ReturnsEmptyList()
    {
        // Arrange & Act
        var result = PolylineDecoder.Decode("");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Decode_NullString_ReturnsEmptyList()
    {
        // Arrange & Act
        var result = PolylineDecoder.Decode(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Decode_SinglePoint_ReturnsOnePoint()
    {
        // Arrange - Encoded point for approximately (38.5, -120.2)
        // This is a well-known test case from Google's polyline documentation
        var encoded = "_p~iF~ps|U";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCount(1);
        result[0].Latitude.Should().BeApproximately(38.5, 0.001);
        result[0].Longitude.Should().BeApproximately(-120.2, 0.001);
    }

    [Fact]
    public void Decode_MultiplePoints_ReturnsCorrectSequence()
    {
        // Arrange - Google's documentation example:
        // Points: (38.5, -120.2), (40.7, -120.95), (43.252, -126.453)
        var encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCount(3);

        result[0].Latitude.Should().BeApproximately(38.5, 0.001);
        result[0].Longitude.Should().BeApproximately(-120.2, 0.001);

        result[1].Latitude.Should().BeApproximately(40.7, 0.001);
        result[1].Longitude.Should().BeApproximately(-120.95, 0.001);

        result[2].Latitude.Should().BeApproximately(43.252, 0.001);
        result[2].Longitude.Should().BeApproximately(-126.453, 0.001);
    }

    [Fact]
    public void Decode_PositiveCoordinates_DecodesCorrectly()
    {
        // Arrange - Simple positive coordinates
        var encoded = "_p~iF_p~iF";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCount(1);
        result[0].Latitude.Should().BeGreaterThan(0);
        result[0].Longitude.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Decode_NegativeCoordinates_DecodesCorrectly()
    {
        // Arrange - The ~ps|U part represents negative longitude
        var encoded = "_p~iF~ps|U";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCount(1);
        result[0].Longitude.Should().BeLessThan(0);
    }

    [Fact]
    public void Decode_LongPolyline_DecodesAllPoints()
    {
        // Arrange - A longer polyline with multiple points
        var encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@_c`|@_seK";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCountGreaterThan(3);

        // Verify first point is correct
        result[0].Latitude.Should().BeApproximately(38.5, 0.001);
        result[0].Longitude.Should().BeApproximately(-120.2, 0.001);
    }

    [Fact]
    public void Decode_ResultPoints_HaveValidLatLon()
    {
        // Arrange
        var encoded = "_p~iF~ps|U_ulLnnqC";

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert - All points should have valid coordinate ranges
        foreach (var point in result)
        {
            point.Latitude.Should().BeInRange(-90, 90);
            point.Longitude.Should().BeInRange(-180, 180);
        }
    }

    [Fact]
    public void Decode_VerySmallDelta_HandlesCorrectly()
    {
        // Arrange - Test case with small coordinate differences
        // Creating a simple two-point line close together
        var encoded = "_p~iF~ps|UA?";  // ? = small delta

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert
        result.Should().HaveCountGreaterOrEqualTo(1);
    }

    #endregion

    #region DecodeToTuples Tests

    [Fact]
    public void DecodeToTuples_EmptyString_ReturnsEmptyList()
    {
        // Arrange & Act
        var result = PolylineDecoder.DecodeToTuples("");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void DecodeToTuples_ValidPolyline_ReturnsTuples()
    {
        // Arrange
        var encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

        // Act
        var result = PolylineDecoder.DecodeToTuples(encoded);

        // Assert
        result.Should().HaveCount(3);
        result[0].Latitude.Should().BeApproximately(38.5, 0.001);
        result[0].Longitude.Should().BeApproximately(-120.2, 0.001);
    }

    [Fact]
    public void DecodeToTuples_MatchesDecode()
    {
        // Arrange
        var encoded = "_p~iF~ps|U_ulLnnqC";

        // Act
        var points = PolylineDecoder.Decode(encoded);
        var tuples = PolylineDecoder.DecodeToTuples(encoded);

        // Assert
        tuples.Should().HaveCount(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            tuples[i].Latitude.Should().Be(points[i].Latitude);
            tuples[i].Longitude.Should().Be(points[i].Longitude);
        }
    }

    #endregion

    #region Real-World Test Cases

    [Fact]
    public void Decode_RealWorldRoute_DecodesCorrectly()
    {
        // Arrange - A short OSRM route segment
        // This represents a simple route that can be verified manually
        var encoded = "mz_eFvinjVnIhK";  // Simple route segment

        // Act
        var result = PolylineDecoder.Decode(encoded);

        // Assert - Should decode to valid points
        result.Should().NotBeEmpty();
        foreach (var point in result)
        {
            point.Latitude.Should().BeInRange(-90, 90);
            point.Longitude.Should().BeInRange(-180, 180);
        }
    }

    #endregion
}
