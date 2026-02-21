using System.Text.Json;

namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// Parses API response bodies to extract results.
/// Extracted from ApiClient for testability (no MAUI dependencies).
/// </summary>
public static class ApiResponseParser
{
    /// <summary>
    /// Parses a successful location response to determine if location was logged or skipped.
    /// </summary>
    /// <remarks>
    /// Handles two server response formats:
    /// <list type="bullet">
    /// <item>log-location: <c>{ "success": true, "skipped": false, "locationId": 123 }</c></item>
    /// <item>check-in: <c>{ "message": "...", "location": { "id": 123, ... } }</c></item>
    /// </list>
    /// See #216 Bug 2: previously only the flat <c>locationId</c> format was parsed,
    /// causing all check-in syncs to return <c>LocationId = null</c>.
    /// </remarks>
    /// <param name="responseBody">The raw JSON response body.</param>
    /// <returns>Parsed API result with message, locationId, and skipped state.</returns>
    public static ApiResponseParseResult Parse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var message = root.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : "Success";

            // Extract locationId — try flat format first (log-location), then nested (check-in)
            int? locationId = root.TryGetProperty("locationId", out var idProp)
                && idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt32()
                    : null;

            // Fallback: check-in returns { "location": { "id": N, ... } }
            if (!locationId.HasValue
                && root.TryGetProperty("location", out var locProp)
                && locProp.ValueKind == JsonValueKind.Object
                && locProp.TryGetProperty("id", out var nestedIdProp)
                && nestedIdProp.ValueKind == JsonValueKind.Number)
            {
                locationId = nestedIdProp.GetInt32();
            }

            // Check if location was skipped due to thresholds
            var skipped = message?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true;

            return new ApiResponseParseResult(message, locationId, skipped);
        }
        catch (JsonException)
        {
            // Non-JSON response or malformed JSON - treat as success with default message
            return new ApiResponseParseResult("Location logged", null, false);
        }
    }
}

/// <summary>
/// Result of parsing an API response body.
/// </summary>
/// <param name="Message">The response message.</param>
/// <param name="LocationId">The server-assigned location ID, or null if not present.</param>
/// <param name="Skipped">Whether the server skipped storing this location (threshold filtering).</param>
public record ApiResponseParseResult(string? Message, int? LocationId, bool Skipped);
