using System.ComponentModel;
using Mapsui;
using Mapsui.UI.Maui;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for viewing groups and member locations.
/// </summary>
public partial class GroupsPage : ContentPage
{
    private readonly GroupsViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of GroupsPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    public GroupsPage(GroupsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to page loaded event for map initialization (like TimelinePage)
        Loaded += OnPageLoaded;

        // Subscribe to property changes for bottom sheet state management
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Called when the page is loaded and controls are initialized.
    /// </summary>
    private void OnPageLoaded(object? sender, EventArgs e)
    {
        // Subscribe to map info events for tap handling after page is loaded
        MapControl.Info += OnMapInfo;

        // Ensure map is set
        if (MapControl.Map == null)
        {
            MapControl.Map = _viewModel.Map;
        }
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the page disappears.
    /// </summary>
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        MapControl.Info -= OnMapInfo;
        await _viewModel.OnDisappearingAsync();
    }

    /// <summary>
    /// Handles map info events for marker tap detection.
    /// </summary>
    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var map = MapControl.Map;
        if (map == null) return;

        var mapInfo = e.GetMapInfo(map.Layers);
        var feature = mapInfo?.Feature;
        if (feature == null) return;

        // Check if a group member marker was tapped
        if (feature["UserId"] is string userId)
        {
            System.Diagnostics.Debug.WriteLine($"[GroupsPage] Member marker tapped: {userId}");
            _viewModel.ShowMemberDetailsByUserId(userId);
            BottomSheet.State = BottomSheetState.HalfExpanded;
        }
    }

    /// <summary>
    /// Handles ViewModel property changes for bottom sheet state.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupsViewModel.IsMemberSheetOpen))
        {
            if (_viewModel.IsMemberSheetOpen && _viewModel.SelectedMember != null)
            {
                BottomSheet.State = BottomSheetState.HalfExpanded;
            }
            else if (!_viewModel.IsMemberSheetOpen)
            {
                BottomSheet.State = BottomSheetState.Collapsed;
            }
        }
    }

    /// <summary>
    /// Handles date picker OK click.
    /// </summary>
    private void OnDatePickerOkClicked(object? sender, EventArgs e)
    {
        _viewModel.DateSelectedCommand.Execute(null);
    }

    /// <summary>
    /// Handles date picker Cancel click.
    /// </summary>
    private void OnDatePickerCancelClicked(object? sender, EventArgs e)
    {
        _viewModel.CancelDatePickerCommand.Execute(null);
    }
}
