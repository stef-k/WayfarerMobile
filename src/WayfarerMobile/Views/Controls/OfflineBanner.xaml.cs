namespace WayfarerMobile.Views.Controls;

/// <summary>
/// Banner control that displays when the device is offline.
/// Automatically monitors connectivity and shows/hides accordingly.
/// </summary>
public partial class OfflineBanner : ContentView
{
    /// <summary>
    /// Bindable property for offline state.
    /// </summary>
    public static readonly BindableProperty IsOfflineProperty =
        BindableProperty.Create(
            nameof(IsOffline),
            typeof(bool),
            typeof(OfflineBanner),
            false,
            BindingMode.OneWay);

    /// <summary>
    /// Gets or sets whether the device is offline.
    /// </summary>
    public bool IsOffline
    {
        get => (bool)GetValue(IsOfflineProperty);
        set => SetValue(IsOfflineProperty, value);
    }

    /// <summary>
    /// Creates a new instance of OfflineBanner.
    /// </summary>
    public OfflineBanner()
    {
        InitializeComponent();

        // Check initial connectivity state
        UpdateConnectivityState();

        // Subscribe to connectivity changes
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>
    /// Handles connectivity change events.
    /// </summary>
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectivityState();
        });
    }

    /// <summary>
    /// Updates the offline state based on current connectivity.
    /// </summary>
    private void UpdateConnectivityState()
    {
        var access = Connectivity.Current.NetworkAccess;
        IsOffline = access != NetworkAccess.Internet && access != NetworkAccess.ConstrainedInternet;
    }

    /// <summary>
    /// Cleanup when the control is unloaded.
    /// </summary>
    ~OfflineBanner()
    {
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }
}
