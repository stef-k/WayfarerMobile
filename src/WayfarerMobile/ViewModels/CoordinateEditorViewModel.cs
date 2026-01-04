using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Interfaces;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for coordinate editing functionality in the timeline.
/// Handles coordinate picking mode, temp marker display, and coordinate save operations.
/// </summary>
public partial class CoordinateEditorViewModel : ObservableObject
{
    private readonly ICoordinateEditorCallbacks _callbacks;
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private readonly ILogger<CoordinateEditorViewModel> _logger;

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether coordinate picking mode is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private bool _isCoordinatePickingMode;

    /// <summary>
    /// Gets or sets the pending latitude during coordinate picking.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingCoordinatesText))]
    private double? _pendingLatitude;

    /// <summary>
    /// Gets or sets the pending longitude during coordinate picking.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingCoordinatesText))]
    private double? _pendingLongitude;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// </summary>
    public bool IsEditing => IsCoordinatePickingMode;

    /// <summary>
    /// Gets whether there are pending coordinates to save.
    /// </summary>
    public bool HasPendingCoordinates => PendingLatitude.HasValue && PendingLongitude.HasValue;

    /// <summary>
    /// Gets the pending coordinates text for display.
    /// </summary>
    public string PendingCoordinatesText => HasPendingCoordinates
        ? $"{PendingLatitude:F6}, {PendingLongitude:F6}"
        : "Tap on map to set location";

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CoordinateEditorViewModel.
    /// </summary>
    /// <param name="callbacks">The callback interface for parent state access.</param>
    /// <param name="timelineSyncService">The timeline sync service.</param>
    /// <param name="toastService">The toast service.</param>
    /// <param name="logger">The logger.</param>
    public CoordinateEditorViewModel(
        ICoordinateEditorCallbacks callbacks,
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ILogger<CoordinateEditorViewModel> logger)
    {
        _callbacks = callbacks;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        _logger = logger;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Enters coordinate picking mode.
    /// </summary>
    [RelayCommand]
    private void EnterCoordinatePickingMode()
    {
        var selectedLocation = _callbacks.SelectedLocation;
        if (selectedLocation == null) return;

        // Set initial pending coordinates to current location
        PendingLatitude = selectedLocation.Latitude;
        PendingLongitude = selectedLocation.Longitude;
        IsCoordinatePickingMode = true;

        // Show temp marker at current location
        UpdateTempMarker(PendingLatitude.Value, PendingLongitude.Value);
    }

    /// <summary>
    /// Sets the pending coordinates from a map tap.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void SetPendingCoordinates(double latitude, double longitude)
    {
        if (!IsCoordinatePickingMode) return;

        PendingLatitude = latitude;
        PendingLongitude = longitude;
        UpdateTempMarker(latitude, longitude);
    }

    /// <summary>
    /// Saves the pending coordinates.
    /// </summary>
    [RelayCommand]
    private async Task SaveCoordinatesAsync()
    {
        var selectedLocation = _callbacks.SelectedLocation;
        if (selectedLocation == null || !HasPendingCoordinates) return;

        // Store locationId before any changes (reference becomes stale after reload)
        var locationId = selectedLocation.LocationId;

        // Check online status
        if (!_callbacks.IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Changes will sync when online.");
        }

        try
        {
            _callbacks.IsBusy = true;

            await _timelineSyncService.UpdateLocationAsync(
                locationId,
                PendingLatitude,
                PendingLongitude,
                localTimestamp: null,
                notes: null,
                includeNotes: false);

            // Exit picking mode
            ExitCoordinatePickingMode();

            // Clear IsBusy BEFORE reload - LoadDataAsync has an IsBusy guard that would skip reload
            _callbacks.IsBusy = false;

            // Reload data to reflect changes on map
            await _callbacks.ReloadTimelineAsync();

            // Re-select the location to show updated details in bottom sheet
            _callbacks.ShowLocationDetails(locationId);
            _callbacks.OpenLocationSheet();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error saving coordinates");
            await _toastService.ShowErrorAsync("Network error. Changes will sync when online.");
            _callbacks.IsBusy = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving coordinates");
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
            _callbacks.IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels coordinate picking mode.
    /// </summary>
    [RelayCommand]
    private void CancelCoordinatePicking()
    {
        ExitCoordinatePickingMode();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Exits coordinate picking mode and cleans up.
    /// </summary>
    private void ExitCoordinatePickingMode()
    {
        IsCoordinatePickingMode = false;
        PendingLatitude = null;
        PendingLongitude = null;
        RemoveTempMarker();
    }

    /// <summary>
    /// Updates or creates the temporary marker at the specified coordinates.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    private void UpdateTempMarker(double latitude, double longitude)
    {
        var tempMarkerLayer = _callbacks.TempMarkerLayer;
        var map = _callbacks.MapInstance;

        if (tempMarkerLayer == null || map == null)
            return;

        tempMarkerLayer.Clear();

        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var point = new NetTopologySuite.Geometries.Point(x, y);
        var feature = new GeometryFeature(point)
        {
            Styles = new[] { CreateTempMarkerStyle() }
        };

        tempMarkerLayer.Add(feature);
        tempMarkerLayer.DataHasChanged();
    }

    /// <summary>
    /// Removes the temporary marker from the map.
    /// </summary>
    private void RemoveTempMarker()
    {
        var tempMarkerLayer = _callbacks.TempMarkerLayer;
        if (tempMarkerLayer == null)
            return;

        tempMarkerLayer.Clear();
        tempMarkerLayer.DataHasChanged();
    }

    /// <summary>
    /// Creates the style for the temporary marker (distinct from regular markers).
    /// </summary>
    private static IStyle CreateTempMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolScale = 0.7,
            Fill = new Brush(Color.FromArgb(255, 255, 87, 34)), // Orange (Material Deep Orange)
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    #endregion
}
