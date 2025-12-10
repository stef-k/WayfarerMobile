using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views.Onboarding;

/// <summary>
/// Page for guiding users through first-run setup and permissions.
/// </summary>
public partial class OnboardingPage : ContentPage
{
    /// <summary>
    /// Creates a new instance of OnboardingPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
