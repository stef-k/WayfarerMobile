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
