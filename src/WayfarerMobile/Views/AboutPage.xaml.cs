using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// About page showing app information and open source licenses.
/// </summary>
public partial class AboutPage : ContentPage
{
    /// <summary>
    /// Creates a new instance of AboutPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
