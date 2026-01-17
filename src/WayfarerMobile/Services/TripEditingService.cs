using Microsoft.Extensions.Logging;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for editing trip metadata in local storage.
/// Handles name and notes updates for downloaded trips.
/// </summary>
public class TripEditingService : ITripEditingService
{
    private readonly ITripRepository _tripRepository;
    private readonly ILogger<TripEditingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TripEditingService"/> class.
    /// </summary>
    public TripEditingService(
        ITripRepository tripRepository,
        ILogger<TripEditingService> logger)
    {
        _tripRepository = tripRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task UpdateTripNameAsync(Guid tripServerId, string newName)
    {
        var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot update trip name - trip {TripId} not found", tripServerId);
            return;
        }

        trip.Name = newName;
        await _tripRepository.SaveDownloadedTripAsync(trip);
        _logger.LogInformation("Updated trip name to '{NewName}' for trip {TripId}", newName, tripServerId);
    }

    /// <inheritdoc/>
    public async Task UpdateTripNotesAsync(Guid tripServerId, string? newNotes)
    {
        var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot update trip notes - trip {TripId} not found", tripServerId);
            return;
        }

        trip.Notes = newNotes;
        await _tripRepository.SaveDownloadedTripAsync(trip);
        _logger.LogInformation("Updated trip notes for trip {TripId}", tripServerId);
    }
}
