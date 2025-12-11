using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Code-behind for the diagnostics page.
/// </summary>
public partial class DiagnosticsPage : ContentPage
{
    private readonly DiagnosticsViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the DiagnosticsPage class.
    /// </summary>
    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDataCommand.ExecuteAsync(null);
    }
}
