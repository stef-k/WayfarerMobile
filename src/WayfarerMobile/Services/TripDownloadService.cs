using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>Persists Trip metadata, places, segments, regions, and polygons for offline use.</summary>
public sealed class TripDownloadService : ITripDownloadService
{
    private readonly IApiClient _apiClient;
    private readonly ITripRepository _trips;
    private readonly IPlaceRepository _places;
    private readonly ISegmentRepository _segments;
    private readonly IAreaRepository _areas;
    private readonly ITripMetadataBuilder _metadataBuilder;
    private readonly ITripContentService _content;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<TripDownloadService> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _activeDownloads = new();

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    public TripDownloadService(IApiClient apiClient, ITripRepository trips, IPlaceRepository places,
        ISegmentRepository segments, IAreaRepository areas, ITripMetadataBuilder metadataBuilder,
        ITripContentService content, IConnectivity connectivity, ILogger<TripDownloadService> logger)
    {
        _apiClient = apiClient;
        _trips = trips;
        _places = places;
        _segments = segments;
        _areas = areas;
        _metadataBuilder = metadataBuilder;
        _content = content;
        _connectivity = connectivity;
        _logger = logger;
    }

    public async Task<DownloadedTripEntity?> DownloadTripAsync(TripSummary tripSummary, CancellationToken cancellationToken = default)
    {
        if (tripSummary.Id == Guid.Empty || _connectivity.NetworkAccess != NetworkAccess.Internet) return null;
        if (!_activeDownloads.TryAdd(tripSummary.Id, 0)) return null;

        DownloadedTripEntity? trip = null;
        var created = false;
        try
        {
            trip = await _trips.GetDownloadedTripByServerIdAsync(tripSummary.Id);
            if (trip?.UnifiedState == UnifiedDownloadState.Downloaded) return trip;

            created = trip is null;
            trip ??= new DownloadedTripEntity { ServerId = tripSummary.Id, DownloadedAt = DateTime.UtcNow };
            trip.Name = tripSummary.Name;
            trip.UnifiedState = UnifiedDownloadState.Downloading;
            CopyBoundingBox(trip, tripSummary.BoundingBox);
            await _trips.SaveDownloadedTripAsync(trip);
            RaiseProgress(trip.Id, 5, "Fetching Trip data…");

            var details = await _apiClient.GetTripDetailsAsync(tripSummary.Id, cancellationToken);
            if (details is null) throw new InvalidOperationException("The Trip data could not be downloaded.");

            var areas = _metadataBuilder.BuildAreas(details);
            var places = _metadataBuilder.BuildPlaces(details);
            var segments = _metadataBuilder.BuildSegments(details);
            var polygons = _metadataBuilder.BuildPolygons(details);
            await _areas.SaveOfflineAreasAsync(trip.Id, areas);
            RaiseProgress(trip.Id, 30, "Saving places…");
            await _places.SaveOfflinePlacesAsync(trip.Id, places);
            RaiseProgress(trip.Id, 60, "Saving routes…");
            await _segments.SaveOfflineSegmentsAsync(trip.Id, segments);
            await _areas.SaveOfflinePolygonsAsync(trip.Id, polygons);

            trip.Name = details.Name;
            trip.PlaceCount = places.Count;
            trip.RegionCount = areas.Count;
            trip.SegmentCount = segments.Count;
            trip.AreaCount = polygons.Count;
            trip.Version = details.Version;
            trip.ServerUpdatedAt = details.UpdatedAt;
            trip.Notes = details.Notes;
            trip.CoverImageUrl = details.CoverImageUrl;
            CopyBoundingBox(trip, details.BoundingBox ?? tripSummary.BoundingBox);
            trip.UnifiedState = UnifiedDownloadState.Downloaded;
            trip.StateChangedAt = DateTime.UtcNow;
            await _trips.SaveDownloadedTripAsync(trip);
            RaiseProgress(trip.Id, 100, "Trip data downloaded");
            return trip;
        }
        catch (OperationCanceledException)
        {
            if (created && trip is not null) await _trips.DeleteDownloadedTripAsync(trip.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Trip data {TripId}", tripSummary.Id);
            if (created && trip is not null) await _trips.DeleteDownloadedTripAsync(trip.Id);
            return null;
        }
        finally
        {
            _activeDownloads.TryRemove(tripSummary.Id, out _);
        }
    }

    public Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync() => _trips.GetDownloadedTripsAsync();

    public async Task<bool> IsTripDownloadedAsync(Guid tripId) =>
        (await _trips.GetDownloadedTripByServerIdAsync(tripId))?.UnifiedState == UnifiedDownloadState.Downloaded;

    public async Task DeleteTripAsync(Guid tripServerId)
    {
        var trip = await _trips.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip is not null) await _trips.DeleteDownloadedTripAsync(trip.Id);
    }

    public Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId) => _content.GetOfflineTripDetailsAsync(tripServerId);
    public Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId) => _content.GetOfflinePlacesAsync(tripServerId);
    public Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId) => _content.GetOfflineSegmentsAsync(tripServerId);

    private void RaiseProgress(int tripId, int percent, string status) => ProgressChanged?.Invoke(this,
        new DownloadProgressEventArgs { TripId = tripId, ProgressPercent = percent, StatusMessage = status });

    private static void CopyBoundingBox(DownloadedTripEntity trip, BoundingBox? box)
    {
        if (box is null) return;
        trip.BoundingBoxNorth = box.North;
        trip.BoundingBoxSouth = box.South;
        trip.BoundingBoxEast = box.East;
        trip.BoundingBoxWest = box.West;
    }
}
