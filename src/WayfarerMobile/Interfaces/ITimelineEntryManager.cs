using WayfarerMobile.Shared.Controls;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Manages timeline entry operations (CRUD and external integrations).
/// </summary>
public interface ITimelineEntryManager
{
    /// <summary>
    /// Saves notes for a location.
    /// </summary>
    /// <param name="locationId">The location ID.</param>
    /// <param name="notesHtml">The notes HTML content.</param>
    /// <returns>True if save succeeded (or queued for offline), false on error.</returns>
    Task<bool> SaveNotesAsync(int locationId, string? notesHtml);

    /// <summary>
    /// Saves entry changes (coordinates, timestamp, notes).
    /// </summary>
    /// <param name="args">The update event args.</param>
    /// <returns>True if save succeeded (or queued for offline), false on error.</returns>
    Task<bool> SaveEntryChangesAsync(TimelineEntryUpdateEventArgs args);

    /// <summary>
    /// Opens coordinates in an external maps app.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <param name="locationName">Display name for the location.</param>
    Task OpenInMapsAsync(double latitude, double longitude, string locationName);

    /// <summary>
    /// Opens Wikipedia geosearch for coordinates.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    Task SearchWikipediaAsync(double latitude, double longitude);

    /// <summary>
    /// Copies coordinates to clipboard.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    Task CopyCoordinatesAsync(double latitude, double longitude);

    /// <summary>
    /// Shares location via system share.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <param name="timeText">Formatted time text.</param>
    /// <param name="dateText">Formatted date text.</param>
    Task ShareLocationAsync(double latitude, double longitude, string timeText, string dateText);
}
