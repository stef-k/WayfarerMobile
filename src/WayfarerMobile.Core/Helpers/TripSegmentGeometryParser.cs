using System.Text.Json;

namespace WayfarerMobile.Core.Helpers;

public enum SegmentGeometryFailure
{
    Empty,
    MalformedGeoJson,
    UnsupportedGeoJsonType,
    MalformedEncodedPolyline,
    InvalidCoordinate,
    InsufficientPoints
}

public sealed record SegmentGeometryParseResult(
    IReadOnlyList<(double Latitude, double Longitude)> Coordinates,
    SegmentGeometryFailure? Failure)
{
    public bool IsSuccess => Failure is null;
}

/// <summary>
/// Parses Segment transport geometry into validated geographic coordinates.
/// </summary>
public static class TripSegmentGeometryParser
{
    private static readonly IReadOnlyList<(double Latitude, double Longitude)> NoCoordinates =
        Array.Empty<(double Latitude, double Longitude)>();

    public static SegmentGeometryParseResult Parse(string? geometry)
    {
        if (string.IsNullOrWhiteSpace(geometry))
            return Failure(SegmentGeometryFailure.Empty);

        var detected = geometry.AsSpan().TrimStart();
        return detected[0] == '{'
            ? ParseGeoJson(detected)
            : ParseEncodedPolyline(geometry);
    }

    private static SegmentGeometryParseResult ParseGeoJson(ReadOnlySpan<char> geometry)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(geometry.ToString());
        }
        catch (JsonException)
        {
            return Failure(SegmentGeometryFailure.MalformedGeoJson);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                return Failure(SegmentGeometryFailure.MalformedGeoJson);
            }

            if (type.GetString() != "LineString")
                return Failure(SegmentGeometryFailure.UnsupportedGeoJsonType);

            if (!root.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array)
            {
                return Failure(SegmentGeometryFailure.MalformedGeoJson);
            }

            var result = new List<(double Latitude, double Longitude)>();
            foreach (var position in coordinates.EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2)
                    return Failure(SegmentGeometryFailure.InvalidCoordinate);

                var ordinates = position.EnumerateArray();
                ordinates.MoveNext();
                var longitudeValue = ordinates.Current;
                ordinates.MoveNext();
                var latitudeValue = ordinates.Current;
                if (longitudeValue.ValueKind != JsonValueKind.Number ||
                    latitudeValue.ValueKind != JsonValueKind.Number ||
                    !longitudeValue.TryGetDouble(out var longitude) ||
                    !latitudeValue.TryGetDouble(out var latitude) ||
                    !IsValidCoordinate(latitude, longitude))
                {
                    return Failure(SegmentGeometryFailure.InvalidCoordinate);
                }

                result.Add((latitude, longitude));
            }

            return result.Count < 2
                ? Failure(SegmentGeometryFailure.InsufficientPoints)
                : Success(result);
        }
    }

    private static SegmentGeometryParseResult ParseEncodedPolyline(string geometry)
    {
        if (!IsStructurallyValidEncodedPolyline(geometry))
            return Failure(SegmentGeometryFailure.MalformedEncodedPolyline);

        var coordinates = PolylineDecoder.DecodeToTuples(geometry);
        if (coordinates.Any(point => !IsValidCoordinate(point.Latitude, point.Longitude)))
            return Failure(SegmentGeometryFailure.InvalidCoordinate);

        return coordinates.Count < 2
            ? Failure(SegmentGeometryFailure.InsufficientPoints)
            : Success(coordinates);
    }

    private static bool IsStructurallyValidEncodedPolyline(string encoded)
    {
        var index = 0;
        long latitude = 0;
        long longitude = 0;

        while (index < encoded.Length)
        {
            if (!TryReadComponent(encoded, ref index, out var latitudeDelta) ||
                !TryReadComponent(encoded, ref index, out var longitudeDelta))
            {
                return false;
            }

            try
            {
                latitude = checked(latitude + latitudeDelta);
                longitude = checked(longitude + longitudeDelta);
            }
            catch (OverflowException)
            {
                return false;
            }

            if (latitude is < -9_000_000 or > 9_000_000 ||
                longitude is < -18_000_000 or > 18_000_000)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadComponent(string encoded, ref int index, out long delta)
    {
        ulong result = 0;
        var shift = 0;

        while (index < encoded.Length)
        {
            var character = encoded[index++];
            if (character is < '?' or > '~')
            {
                delta = 0;
                return false;
            }

            var value = character - 63;
            if (shift > 30 || (shift == 30 && (value & 0x1f) > 1))
            {
                delta = 0;
                return false;
            }

            result |= (ulong)(value & 0x1f) << shift;
            if (value < 0x20)
            {
                delta = (result & 1) == 0
                    ? (long)(result >> 1)
                    : -(long)(result >> 1) - 1;
                return true;
            }

            shift += 5;
        }

        delta = 0;
        return false;
    }

    private static bool IsValidCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) &&
        double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 &&
        longitude is >= -180 and <= 180;

    private static SegmentGeometryParseResult Success(
        IReadOnlyList<(double Latitude, double Longitude)> coordinates) => new(coordinates, null);

    private static SegmentGeometryParseResult Failure(SegmentGeometryFailure failure) =>
        new(NoCoordinates, failure);
}
