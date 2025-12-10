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
}
