using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class TripSegmentGeometryParserTests
{
    [Fact]
    public void Parse_ApiGeoJsonLineString_PreservesOrderedLongitudeLatitudeCoordinates()
    {
        const string geometry = """
            {"type":"LineString","coordinates":[[23.7275,37.9838],[23.7281,37.9844,42]]}
            """;

        var result = TripSegmentGeometryParser.Parse(geometry);

        result.IsSuccess.Should().BeTrue();
        result.Failure.Should().BeNull();
        result.Coordinates.Should().BeEquivalentTo(
            [(37.9838, 23.7275), (37.9844, 23.7281)],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Parse_EncodedPolyline_PreservesExistingPrecisionAndOrder()
    {
        var result = TripSegmentGeometryParser.Parse("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

        result.IsSuccess.Should().BeTrue();
        result.Coordinates.Should().BeEquivalentTo(
            [(38.5, -120.2), (40.7, -120.95), (43.252, -126.453)],
            options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(null, SegmentGeometryFailure.Empty)]
    [InlineData("   ", SegmentGeometryFailure.Empty)]
    [InlineData("{not json", SegmentGeometryFailure.MalformedGeoJson)]
    [InlineData("{\"type\":\"Point\",\"coordinates\":[1,2]}", SegmentGeometryFailure.UnsupportedGeoJsonType)]
    [InlineData("{\"type\":\"LineString\",\"coordinates\":[[1,2]]}", SegmentGeometryFailure.InsufficientPoints)]
    [InlineData("{\"type\":\"LineString\",\"coordinates\":[[181,2],[3,4]]}", SegmentGeometryFailure.InvalidCoordinate)]
    [InlineData("_p~iF", SegmentGeometryFailure.MalformedEncodedPolyline)]
    [InlineData("_p~iF~ps|U", SegmentGeometryFailure.InsufficientPoints)]
    public void Parse_InvalidGeometry_ReturnsBoundedFailure(string? geometry, SegmentGeometryFailure expected)
    {
        var result = TripSegmentGeometryParser.Parse(geometry);

        result.IsSuccess.Should().BeFalse();
        result.Failure.Should().Be(expected);
        result.Coordinates.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedGeoJson_DoesNotFallThroughToEncodedPolyline()
    {
        var result = TripSegmentGeometryParser.Parse("   {????????????");

        result.Failure.Should().Be(SegmentGeometryFailure.MalformedGeoJson);
    }

    [Fact]
    public void Parse_PositiveNegativeAndAntimeridianValues_PreservesMeaningAndOrder()
    {
        const string geometry = """
            {"type":"LineString","coordinates":[[179.9999,-45.25],[-179.9998,45.5]]}
            """;

        var result = TripSegmentGeometryParser.Parse(geometry);

        result.Coordinates.Should().BeEquivalentTo(
            [(-45.25, 179.9999), (45.5, -179.9998)],
            options => options.WithStrictOrdering());
    }
}
