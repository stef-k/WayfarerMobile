using System.ComponentModel;
using Mapsui;
using Mapsui.Projections;
using Mapsui.UI.Maui;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.Core.Models;
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
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        // Initialize map only once when page is first loaded
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
        if (_viewModel.CoordinateEditor.IsCoordinatePickingMode)
        {
            if (mapInfo?.WorldPosition != null)
            {
                var worldPos = mapInfo.WorldPosition;
                var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);
                _viewModel.CoordinateEditor.SetPendingCoordinates(lonLat.lat, lonLat.lon);
            }
            return;
        }

        // Normal mode: handle feature taps
        var feature = mapInfo?.Feature;
        if (feature == null) return;

        if (feature["TimelineIdentity"] is TimelineEntryIdentity identity)
        {
            _viewModel.ShowLocationDetails(identity);
            if (_viewModel.SelectedLocation != null)
                BottomSheet.State = BottomSheetState.FullExpanded;
        }
    }

    private async void OnEditLocationClicked(object? sender, EventArgs e)
    {
        if (_viewModel.SelectedLocation?.CanEdit != true) return;

        var identity = _viewModel.SelectedLocation.Identity;

        var action = await DisplayActionSheetAsync(
            "Edit Location",
            "Cancel",
            "Delete",
            "Adjust Coordinates",
            "Edit Date/Time",
            "Edit Activity",
            "Edit Notes");

        if (_viewModel.SelectedLocation?.Identity != identity) return;

        switch (action)
        {
            case "Adjust Coordinates":
                StartCoordinatePicking();
                break;
            case "Edit Date/Time":
                StartDateTimeEdit();
                break;
            case "Edit Activity":
                ShowActivityPicker();
                break;
            case "Edit Notes":
                await NavigateToNotesEditor();
                break;
            case "Delete":
                await DeleteLocationAsync();
                break;
        }
    }

    private async Task DeleteLocationAsync()
    {
        if (_viewModel.SelectedLocation?.CanEdit != true) return;
        var identity = _viewModel.SelectedLocation.Identity;

        var confirm = await DisplayAlertAsync(
            "Delete Location",
            "Are you sure you want to delete this location? This cannot be undone.",
            "Delete",
            "Cancel");

        if (confirm && _viewModel.SelectedLocation?.Identity == identity)
        {
            await _viewModel.DeleteLocationAsync(identity);
        }
    }

    private void StartCoordinatePicking()
    {
        // Collapse the bottom sheet to minimal height to reveal the map
        BottomSheet.State = BottomSheetState.Collapsed;

        // Enter coordinate picking mode
        _viewModel.CoordinateEditor.EnterCoordinatePickingModeCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // When coordinate picking mode ends (save or cancel), fully expand the bottom sheet
        if (e.PropertyName == "CoordinateEditor.IsCoordinatePickingMode")
        {
            if (!_viewModel.CoordinateEditor.IsCoordinatePickingMode && _viewModel.IsLocationSheetOpen)
            {
                BottomSheet.State = BottomSheetState.FullExpanded;
            }
        }
    }

    private void StartDateTimeEdit()
    {
        // Open the SfDateTimePicker via ViewModel command
        _viewModel.DateTimeEditor.OpenEditDateTimePickerCommand.Execute(null);
    }

    private void ShowActivityPicker()
    {
        // Open the activity picker popup via ViewModel command
        _viewModel.OpenActivityPickerCommand.Execute(null);
    }

    private async Task NavigateToNotesEditor()
    {
        var navParams = _viewModel.PrepareNotesEditorNavigation();
        if (navParams == null) return;
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
        _viewModel.DateTimeEditor.SaveEditDateTimeCommand.Execute(null);
    }

    private void OnEditDateTimePickerCancelClicked(object? sender, EventArgs e)
    {
        // User cancelled - close the picker (binding handles IsEditDateTimePickerOpen)
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Re-subscribe event handlers (unsubscribed in OnDisappearing)
        MapControl.Info += OnMapInfo;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

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
