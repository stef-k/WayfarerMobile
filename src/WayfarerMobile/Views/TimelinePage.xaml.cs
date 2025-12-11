using WayfarerMobile.Shared.Controls;
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

    /// <summary>
    /// Called when the entry details sheet is closed.
    /// </summary>
    private void OnEntryDetailsSheetClosed(object? sender, EventArgs e)
    {
        _viewModel.CloseEntryDetails();
    }

    /// <summary>
    /// Called when entry details save is requested.
    /// </summary>
    private async void OnEntryDetailsSaveRequested(object? sender, TimelineEntryUpdateEventArgs e)
    {
        await _viewModel.SaveEntryChangesAsync(e);
    }

    /// <summary>
    /// Called when the date picker OK button is clicked.
    /// </summary>
    private async void OnDatePickerOkClicked(object? sender, EventArgs e)
    {
        var selectedDate = DatePicker.SelectedDate;
        await _viewModel.DateSelectedCommand.ExecuteAsync(selectedDate);
    }

    /// <summary>
    /// Called when the date picker Cancel button is clicked.
    /// </summary>
    private void OnDatePickerCancelClicked(object? sender, EventArgs e)
    {
        _viewModel.CloseDatePickerCommand.Execute(null);
    }
}
