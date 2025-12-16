using Mapsui;
using Mapsui.UI.Maui;
using Syncfusion.Maui.Toolkit.BottomSheet;
using Syncfusion.Maui.Toolkit.Picker;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

public partial class TimelinePage : ContentPage
{
    private readonly TimelineViewModel _viewModel;

    public TimelinePage(TimelineViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        Loaded += OnPageLoaded;
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
        await DisplayAlertAsync("Edit Location", "Notes editor coming soon", "OK");
    }

    private void OnDatePickerOkClicked(object? sender, EventArgs e)
    {
        var selectedDate = DatePicker.SelectedDate;
        _viewModel.DateSelectedCommand.Execute(selectedDate);
    }

    private void OnDatePickerCancelClicked(object? sender, EventArgs e)
    {
        _viewModel.IsDatePickerOpen = false;
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
        await _viewModel.OnDisappearingAsync();
    }
}
