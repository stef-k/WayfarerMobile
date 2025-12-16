using System.ComponentModel;
using Mapsui;
using Mapsui.Projections;
using Mapsui.UI.Maui;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Timeline page showing location history on a map with editing capabilities.
/// </summary>
public partial class TimelinePage : ContentPage
{
    private readonly TimelineViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of TimelinePage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public TimelinePage(TimelineViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        Loaded += OnPageLoaded;

        // Subscribe to coordinate picking mode changes to manage bottom sheet state
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        MapControl.Info += OnMapInfo;

        if (MapControl.Map == null)
        {
            MapControl.Map = _viewModel.Map;
        }
    }

    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var map = MapControl.Map;
        if (map == null) return;

        var mapInfo = e.GetMapInfo(map.Layers);

        // In coordinate picking mode, handle any tap on the map
        if (_viewModel.IsCoordinatePickingMode)
        {
            if (mapInfo?.WorldPosition != null)
            {
                var worldPos = mapInfo.WorldPosition;
                var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
                _viewModel.SetPendingCoordinates(lonLat.lat, lonLat.lon);
            }
            return;
        }

        // Normal mode: handle feature taps
        var feature = mapInfo?.Feature;
        if (feature == null) return;

        if (feature["LocationId"] is int locationId)
        {
            _viewModel.ShowLocationDetails(locationId);
            BottomSheet.State = BottomSheetState.FullExpanded;
        }
    }

    private async void OnEditLocationClicked(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedLocation == null) return;

        var action = await DisplayActionSheetAsync(
            "Edit Location",
            "Cancel",
            null,
            "Adjust Coordinates",
            "Edit Date/Time",
            "Edit Notes");

        switch (action)
        {
            case "Adjust Coordinates":
                StartCoordinatePicking();
                break;
            case "Edit Date/Time":
                StartDateTimeEdit();
                break;
            case "Edit Notes":
                await NavigateToNotesEditor();
                break;
        }
    }

    private void StartCoordinatePicking()
    {
        // Collapse the bottom sheet to minimal height to reveal the map
        BottomSheet.State = BottomSheetState.Collapsed;

        // Enter coordinate picking mode
        _viewModel.EnterCoordinatePickingModeCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When coordinate picking mode ends (save or cancel), fully expand the bottom sheet
        if (e.PropertyName == nameof(TimelineViewModel.IsCoordinatePickingMode))
        {
            if (!_viewModel.IsCoordinatePickingMode && _viewModel.IsLocationSheetOpen)
            {
                BottomSheet.State = BottomSheetState.FullExpanded;
            }
        }
    }

    private void StartDateTimeEdit()
    {
        // Open the SfDateTimePicker via ViewModel command
        _viewModel.OpenEditDateTimePickerCommand.Execute(null);
    }

    private async Task NavigateToNotesEditor()
    {
        if (_viewModel.SelectedLocation == null) return;

        // Store location ID to reopen sheet when returning
        var locationId = _viewModel.SelectedLocation.LocationId;
        _viewModel.SetPendingLocationToReopen(locationId);

        // Navigate to notes editor page with location ID and current notes
        var navParams = new Dictionary<string, object>
        {
            { "locationId", locationId },
            { "notes", _viewModel.SelectedLocation.Notes ?? string.Empty }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
    }

    private void OnDatePickerOkClicked(object? sender, EventArgs e)
    {
        // User confirmed date selection - navigate to selected date
        // SelectedDate is already updated via two-way binding
        _viewModel.DateSelectedCommand.Execute(null);
    }

    private void OnDatePickerCancelClicked(object? sender, EventArgs e)
    {
        // User cancelled - restore the original date and close the picker
        _viewModel.CancelDatePickerCommand.Execute(null);
    }

    private void OnEditDateTimePickerOkClicked(object? sender, EventArgs e)
    {
        // User confirmed datetime selection - save the edited datetime
        _viewModel.SaveEditDateTimeCommand.Execute(null);
    }

    private void OnEditDateTimePickerCancelClicked(object? sender, EventArgs e)
    {
        // User cancelled - close the picker (binding handles IsEditDateTimePickerOpen)
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        MapControl.Info -= OnMapInfo;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        await _viewModel.OnDisappearingAsync();
    }
}
