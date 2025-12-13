using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views.Onboarding;

/// <summary>
/// Page for guiding users through first-run setup and permissions.
/// </summary>
public partial class OnboardingPage : ContentPage
{
    private readonly OnboardingViewModel _viewModel;
    private readonly IAppLifecycleService? _lifecycleService;

    /// <summary>
    /// Creates a new instance of OnboardingPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="lifecycleService">The app lifecycle service.</param>
    public OnboardingPage(OnboardingViewModel viewModel, IAppLifecycleService lifecycleService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _lifecycleService = lifecycleService;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Called when the page appears. Refreshes permission states in case
    /// they changed while away (e.g., returning from QR scanner or settings).
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshStateCommand.Execute(null);

        // Subscribe to app resume event to catch returning from system dialogs
        if (_lifecycleService != null)
        {
            _lifecycleService.AppResuming += OnAppResuming;
        }
    }

    /// <summary>
    /// Called when the page disappears.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Unsubscribe from app resume event
        if (_lifecycleService != null)
        {
            _lifecycleService.AppResuming -= OnAppResuming;
        }
    }

    /// <summary>
    /// Called when the app resumes from background.
    /// Refreshes permission states that may have changed in system settings.
    /// </summary>
    private void OnAppResuming(object? sender, EventArgs e)
    {
        // Refresh on main thread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _viewModel.RefreshStateCommand.Execute(null);
        });
    }
}
