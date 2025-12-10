using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page displaying location history timeline with date filtering.
/// </summary>
public partial class TimelinePage : ContentPage
{
    private readonly TimelineViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of TimelinePage.
    /// </summary>
    /// <param name="viewModel">The timeline view model.</param>
    public TimelinePage(TimelineViewModel viewModel)
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
