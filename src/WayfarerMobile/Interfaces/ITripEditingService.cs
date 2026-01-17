namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for editing trip metadata in local storage.
/// Handles name and notes updates for downloaded trips.
/// </summary>
public interface ITripEditingService
{
    /// <summary>
    /// Updates a trip's name in local storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="newName">The new trip name.</param>
    Task UpdateTripNameAsync(Guid tripServerId, string newName);

    /// <summary>
    /// Updates a trip's notes in local storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="newNotes">The new trip notes (HTML).</param>
    Task UpdateTripNotesAsync(Guid tripServerId, string? newNotes);
}
