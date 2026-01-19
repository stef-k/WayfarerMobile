namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for exporting queue data to various formats.
/// </summary>
public interface IQueueExportService
{
    /// <summary>
    /// Exports queue to CSV format.
    /// </summary>
    /// <returns>CSV content as string.</returns>
    Task<string> ExportToCsvAsync();

    /// <summary>
    /// Exports queue to GeoJSON format.
    /// </summary>
    /// <returns>GeoJSON content as string.</returns>
    Task<string> ExportToGeoJsonAsync();

    /// <summary>
    /// Exports and opens share dialog.
    /// </summary>
    /// <param name="format">"csv" or "geojson"</param>
    Task ShareExportAsync(string format);
}
