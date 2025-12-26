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
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Fire-and-forget: let page render immediately, show loading indicator while data loads
        _ = _viewModel.LoadDataCommand.ExecuteAsync(null);
    }
}
