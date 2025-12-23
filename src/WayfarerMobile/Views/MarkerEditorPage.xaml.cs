using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for editing place marker color and icon.
/// </summary>
public partial class MarkerEditorPage : ContentPage
{
    /// <summary>
    /// Creates a new instance of MarkerEditorPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    public MarkerEditorPage(MarkerEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
