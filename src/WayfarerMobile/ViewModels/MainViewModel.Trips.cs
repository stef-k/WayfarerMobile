using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

public partial class MainViewModel
{
    private TripDetails? _pendingTrip;

    public void QueueTripForNavigation(TripDetails trip) => _pendingTrip = trip;

    public async Task LoadPendingTripIfReadyAsync(Task readiness, CancellationToken cancellationToken)
    {
        if (_pendingTrip == null)
        {
            _logger.LogDebug("LoadPendingTripIfReadyAsync: No pending trip");
            return;
        }

        var trip = _pendingTrip;
        _logger.LogDebug("LoadPendingTripIfReadyAsync: Waiting for readiness gate, trip={TripName}", trip.Name);

        try
        {
            // Wait for the deterministic readiness gate with cancellation support
            // This will complete when TrySetPageReady() signals all conditions are met
            using var reg = cancellationToken.Register(() =>
                _logger.LogDebug("LoadPendingTripIfReadyAsync: Readiness gate cancelled (page disappeared)"));

            await readiness.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Page disappeared while waiting - keep pending trip for retry on next appear
            _logger.LogDebug("LoadPendingTripIfReadyAsync: Cancelled, keeping pending trip for retry");
            return;
        }

        // Re-check that pending trip is still set (another caller may have processed it)
        if (_pendingTrip == null)
        {
            _logger.LogDebug("LoadPendingTripIfReadyAsync: Pending trip was cleared by another caller");
            return;
        }

        // Clear pending trip now that we're committed to loading
        _pendingTrip = null;

        _logger.LogDebug("LoadPendingTripIfReadyAsync: Readiness gate passed, loading trip {TripName}", trip.Name);
        await LoadTripForNavigationAsync(trip);
        _logger.LogDebug("LoadPendingTripIfReadyAsync: After load, HasLoadedTrip={HasLoaded}", HasLoadedTrip);
    }

    #region Trip Management

    /// <summary>
    /// Loads a trip for navigation.
    /// </summary>
    /// <param name="tripDetails">The trip details to load.</param>
    public async Task LoadTripForNavigationAsync(TripDetails tripDetails)
    {
        // Issue #185 instrumentation: Log visibility state to help diagnose crashes
        _logger.LogDebug("Loading trip: {TripName} ({PlaceCount} places, {SegmentCount} segments, {AreaCount} areas), IsPageVisible={IsVisible}",
            tripDetails.Name, tripDetails.AllPlaces.Count, tripDetails.Segments.Count, tripDetails.AllAreas.Count, _isPageVisible);

        // Debug: Log regions and their areas
        _logger.LogDebug("Trip has {RegionCount} regions", tripDetails.Regions.Count);
        foreach (var region in tripDetails.Regions)
        {
            _logger.LogDebug("Region '{Name}': {PlaceCount} places, {AreaCount} areas",
                region.Name, region.Places.Count, region.Areas.Count);
        }

        // Debug: Log place coordinates to verify data
        foreach (var place in tripDetails.AllPlaces.Take(5))
        {
            _logger.LogDebug("Place '{Name}': Lat={Lat}, Lon={Lon}, Icon={Icon}",
                place.Name, place.Latitude, place.Longitude, place.Icon ?? "null");
        }

        // Reset any previous selection state
        TripSheet.ClearTripSheetSelection();

        // Set loaded trip via ITripStateManager (source of truth)
        _tripStateManager.SetLoadedTrip(tripDetails);
        _logger.LogDebug("After SetLoadedTrip: HasLoadedTrip={HasTrip}, TripSheet.HasLoadedTrip={TsHasTrip}",
            HasLoadedTrip, TripSheet.HasLoadedTrip);
        _tripNavigationService.LoadTrip(tripDetails);

        // Show trip layers on map
        var placePoints = await MapDisplay.ShowTripLayersAsync(tripDetails);
        _logger.LogDebug("Updated {Count} places on map layer (from {Total} total)", placePoints.Count, tripDetails.AllPlaces.Count);

        // Zoom map to fit all trip places
        if (placePoints.Count > 0)
        {
            MapDisplay.ZoomToPoints(placePoints);
            MapDisplay.IsFollowingLocation = false; // Don't auto-center on user location
            _logger.LogDebug("Zoomed map to fit {Count} trip places", placePoints.Count);
        }
        else if (tripDetails.BoundingBox != null)
        {
            // Fallback: use trip bounding box center
            var bb = tripDetails.BoundingBox;
            var centerLat = (bb.North + bb.South) / 2;
            var centerLon = (bb.East + bb.West) / 2;
            MapDisplay.CenterOnLocation(centerLat, centerLon, zoomLevel: 12);
            MapDisplay.IsFollowingLocation = false;
            _logger.LogDebug("Centered map on trip bounding box center");
        }

        // Force map refresh to ensure layers are rendered
        MapDisplay.RefreshMap();

        // Ensure HasLoadedTrip is set (should already be set via OnTripSheetPropertyChanged,
        // but set directly here as well to ensure binding updates during navigation)
        HasLoadedTrip = true;
        OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>
    /// Refreshes the trip display on the map.
    /// Call this after modifying places, areas, or segments.
    /// </summary>
    private async Task RefreshTripOnMapAsync()
    {
        if (TripSheet.LoadedTrip == null)
            return;

        await MapDisplay.RefreshTripLayersAsync(TripSheet.LoadedTrip);
        MapDisplay.RefreshMap();
    }

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    public void UnloadTrip()
    {
        if (Navigation.IsNavigating)
        {
            Navigation.StopNavigation();
        }

        // Clear loaded trip via ITripStateManager (source of truth)
        _tripStateManager.SetLoadedTrip(null);
        HasLoadedTrip = false;
        TripSheet.SelectedPlace = null;
        _tripNavigationService.UnloadTrip();

        // Clear all trip layers
        MapDisplay.ClearTripLayers();

        // Recenter map on user location at street level
        var location = CurrentLocation ?? _locationBridge.LastLocation;
        if (location != null)
        {
            MapDisplay.CenterOnLocation(location.Latitude, location.Longitude, zoomLevel: 16);
        }
    }

    #endregion

}
