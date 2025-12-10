using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Lock screen page for PIN entry when app resumes with protection enabled.
/// </summary>
public partial class LockScreenPage : ContentPage
{
    private readonly LockScreenViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of LockScreenPage.
    /// </summary>
    /// <param name="viewModel">The lock screen view model.</param>
    public LockScreenPage(LockScreenViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to session unlocked event
        _viewModel.SessionUnlocked += OnSessionUnlocked;

        // Subscribe to shake error for animation
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Reset();
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
    /// Handles view model property changes for animations.
    /// </summary>
    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LockScreenViewModel.ShakeError) && _viewModel.ShakeError)
        {
            await PlayShakeAnimation();
        }
    }

    /// <summary>
    /// Plays a shake animation on the PIN dots container for invalid PIN feedback.
    /// </summary>
    private async Task PlayShakeAnimation()
    {
        var originalX = PinDotsContainer.TranslationX;

        // Quick horizontal shake animation
        await PinDotsContainer.TranslateToAsync(originalX - 15, 0, 50);
        await PinDotsContainer.TranslateToAsync(originalX + 15, 0, 50);
        await PinDotsContainer.TranslateToAsync(originalX - 10, 0, 50);
        await PinDotsContainer.TranslateToAsync(originalX + 10, 0, 50);
        await PinDotsContainer.TranslateToAsync(originalX - 5, 0, 50);
        await PinDotsContainer.TranslateToAsync(originalX, 0, 50);
    }

    /// <summary>
    /// Handles successful session unlock.
    /// </summary>
    private async void OnSessionUnlocked(object? sender, EventArgs e)
    {
        // Navigate back or dismiss the lock screen
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Cleanup when page is unloaded.
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler == null)
        {
            // Page is being unloaded, cleanup subscriptions
            _viewModel.SessionUnlocked -= OnSessionUnlocked;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }
}
