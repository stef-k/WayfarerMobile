using Syncfusion.Maui.Toolkit.Expander;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Settings page for app configuration.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of SettingsPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    public SettingsPage(SettingsViewModel viewModel)
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
    /// Called when the Offline Queue expander is expanding.
    /// Refreshes status data when the section is opened.
    /// </summary>
    private async void OnOfflineQueueExpanding(object sender, ExpandingAndCollapsingEventArgs e)
    {
        // The event fires when expanding - refresh data
        await _viewModel.OfflineQueue.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Called when the queue limit entry loses focus.
    /// Applies and validates the new queue limit.
    /// </summary>
    private async void OnQueueLimitEntryUnfocused(object sender, FocusEventArgs e)
    {
        await _viewModel.OfflineQueue.ApplyQueueLimitCommand.ExecuteAsync(null);
    }
}
