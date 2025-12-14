using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// JSON converter that ensures DateTime values are serialized as UTC with "Z" suffix.
/// This guarantees the server correctly identifies timestamps as UTC and avoids
/// double timezone conversion when the server assumes unspecified timestamps are local time.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <summary>
    /// ISO 8601 UTC format with milliseconds and Z suffix.
    /// </summary>
    private const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>
    /// Reads a DateTime from JSON, treating it as UTC if no timezone is specified.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert to.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>A DateTime value in UTC.</returns>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateString = reader.GetString();
        if (string.IsNullOrEmpty(dateString))
            return DateTime.MinValue;

        // Parse with RoundtripKind to preserve timezone info from the string
        if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            // If parsed as Unspecified, treat as UTC (defensive for incoming data)
            return result.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(result, DateTimeKind.Utc)
                : result.ToUniversalTime();
        }

        return DateTime.MinValue;
    }

    /// <summary>
    /// Writes a DateTime to JSON as UTC with "Z" suffix.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The DateTime value to write.</param>
    /// <param name="options">Serializer options.</param>
    /// <remarks>
    /// <para>
    /// <strong>DateTimeKind Handling:</strong>
    /// </para>
    /// <list type="bullet">
    ///   <item><c>DateTimeKind.Utc</c> - Used as-is</item>
    ///   <item><c>DateTimeKind.Unspecified</c> - Treated as UTC (not local time)</item>
    ///   <item><c>DateTimeKind.Local</c> - Converted to UTC</item>
    /// </list>
    /// <para>
    /// We treat Unspecified as UTC because timestamps in this app originate from
    /// Android's location.Time (Unix epoch milliseconds) converted to UTC, but SQLite
    /// doesn't preserve DateTimeKind when storing/retrieving DateTime values. If we
    /// called .ToUniversalTime() on Unspecified, it would incorrectly treat the value
    /// as local time and apply timezone conversion, causing timestamp drift.
    /// </para>
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Determine the UTC value based on DateTimeKind
        DateTime utcValue;
        switch (value.Kind)
        {
            case DateTimeKind.Utc:
                // Already UTC, use as-is
                utcValue = value;
                break;

            case DateTimeKind.Unspecified:
                // CRITICAL: Treat Unspecified as UTC, not local time.
                // Our timestamps originate from Android location.Time (UTC) but SQLite
                // loses DateTimeKind. Calling .ToUniversalTime() would incorrectly
                // apply local timezone conversion.
                utcValue = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                break;

            case DateTimeKind.Local:
                // Convert local time to UTC
                utcValue = value.ToUniversalTime();
                break;

            default:
                utcValue = value;
                break;
        }

        writer.WriteStringValue(utcValue.ToString(UtcFormat, CultureInfo.InvariantCulture));
    }
}
