using WayfarerMobile.Shared.Controls;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for viewing and managing trips.
/// </summary>
public partial class TripsPage : ContentPage
{
    private readonly TripsViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of TripsPage.
    /// </summary>
    /// <param name="viewModel">The trips view model.</param>
    public TripsPage(TripsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
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
        await _viewModel.OnDisappearingAsync();
    }

    /// <summary>
    /// Called when the place details sheet is closed.
    /// </summary>
    private void OnPlaceDetailsSheetClosed(object? sender, EventArgs e)
    {
        _viewModel.ClosePlaceDetails();
    }

    /// <summary>
    /// Called when place details save is requested.
    /// </summary>
    private async void OnPlaceDetailsSaveRequested(object? sender, PlaceUpdateEventArgs e)
    {
        await _viewModel.SavePlaceChangesAsync(e);
    }
}
