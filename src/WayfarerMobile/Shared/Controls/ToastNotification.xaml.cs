namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Toast notification control for displaying temporary messages.
/// Add this control to pages where you want to show toasts.
/// </summary>
public partial class ToastNotification : ContentView
{
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Creates a new instance of ToastNotification.
    /// </summary>
    public ToastNotification()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows an info toast message.
    /// </summary>
    public Task ShowAsync(string message, int durationMs = 3000)
    {
        return ShowToastAsync(message, ToastType.Info, durationMs);
    }

    /// <summary>
    /// Shows a success toast message.
    /// </summary>
    public Task ShowSuccessAsync(string message, int durationMs = 3000)
    {
        return ShowToastAsync(message, ToastType.Success, durationMs);
    }

    /// <summary>
    /// Shows an error toast message.
    /// </summary>
    public Task ShowErrorAsync(string message, int durationMs = 4000)
    {
        return ShowToastAsync(message, ToastType.Error, durationMs);
    }

    /// <summary>
    /// Shows a warning toast message.
    /// </summary>
    public Task ShowWarningAsync(string message, int durationMs = 3500)
    {
        return ShowToastAsync(message, ToastType.Warning, durationMs);
    }

    private async Task ShowToastAsync(string message, ToastType type, int durationMs)
    {
        // Cancel any existing toast
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        // Set content
        MessageLabel.Text = message;

        // Set style based on type
        var (backgroundColor, icon) = type switch
        {
            ToastType.Success => (Color.FromArgb("#4CAF50"), "✓"),
            ToastType.Error => (Color.FromArgb("#F44336"), "✕"),
            ToastType.Warning => (Color.FromArgb("#FF9800"), "⚠"),
            _ => (Color.FromArgb("#323232"), "ℹ")
        };

        ToastBorder.BackgroundColor = backgroundColor;
        IconLabel.Text = icon;

        // Show
        IsVisible = true;
        ToastBorder.Opacity = 0;
        ToastBorder.TranslationY = 50;

        try
        {
            // Animate in
            var fadeInTask = ToastBorder.FadeToAsync(1, 150, Easing.CubicOut);
            var translateInTask = ToastBorder.TranslateToAsync(0, 0, 150, Easing.CubicOut);
            await Task.WhenAll(fadeInTask, translateInTask);

            if (token.IsCancellationRequested) return;

            // Wait
            await Task.Delay(durationMs, token);

            if (token.IsCancellationRequested) return;

            // Animate out
            var fadeOutTask = ToastBorder.FadeToAsync(0, 150, Easing.CubicIn);
            var translateOutTask = ToastBorder.TranslateToAsync(0, -20, 150, Easing.CubicIn);
            await Task.WhenAll(fadeOutTask, translateOutTask);

            IsVisible = false;
        }
        catch (TaskCanceledException)
        {
            // Toast was replaced by a new one
        }
    }

    private enum ToastType
    {
        Info,
        Success,
        Error,
        Warning
    }
}
