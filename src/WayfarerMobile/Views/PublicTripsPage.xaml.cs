using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for browsing and cloning public trips.
/// </summary>
public partial class PublicTripsPage : ContentPage
{
    private readonly PublicTripsViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of PublicTripsPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public PublicTripsPage(PublicTripsViewModel viewModel)
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
}
