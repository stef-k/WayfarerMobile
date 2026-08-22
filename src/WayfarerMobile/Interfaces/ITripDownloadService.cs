using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

/// <summary>Downloads and manages provider-independent offline Trip data.</summary>
public interface ITripDownloadService
{
    event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
    Task<DownloadedTripEntity?> DownloadTripAsync(TripSummary tripSummary, CancellationToken cancellationToken = default);
    Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync();
    Task<bool> IsTripDownloadedAsync(Guid tripId);
    Task DeleteTripAsync(Guid tripServerId);
    Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId);
    Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId);
    Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId);
}
