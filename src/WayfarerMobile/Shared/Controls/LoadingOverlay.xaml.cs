namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Reusable loading overlay control with optional message.
/// Displays a centered spinner with semi-transparent background.
/// </summary>
public partial class LoadingOverlay : ContentView
{
    /// <summary>
    /// Bindable property for the loading message.
    /// </summary>
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message),
        typeof(string),
        typeof(LoadingOverlay),
        default(string));

    /// <summary>
    /// Gets or sets the loading message displayed below the spinner.
    /// </summary>
    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Creates a new instance of LoadingOverlay.
    /// </summary>
    public LoadingOverlay()
    {
        InitializeComponent();
    }
}
