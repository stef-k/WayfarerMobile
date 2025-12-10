using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for manual location check-in.
/// </summary>
public partial class CheckInPage : ContentPage
{
    private readonly CheckInViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of CheckInPage.
    /// </summary>
    /// <param name="viewModel">The check-in view model.</param>
    public CheckInPage(CheckInViewModel viewModel)
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
