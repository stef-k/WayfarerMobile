namespace WayfarerMobile.Views.Controls;

/// <summary>
/// Context menu control for map location actions.
/// Displays options like Navigate To, Share, Wikipedia search, etc.
/// </summary>
public partial class PlaceContextMenu : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// The latitude of the selected location.
    /// </summary>
    public static readonly BindableProperty LatitudeProperty =
        BindableProperty.Create(nameof(Latitude), typeof(double), typeof(PlaceContextMenu), 0.0,
            propertyChanged: OnCoordinatesChanged);

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude
    {
        get => (double)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    /// <summary>
    /// The longitude of the selected location.
    /// </summary>
    public static readonly BindableProperty LongitudeProperty =
        BindableProperty.Create(nameof(Longitude), typeof(double), typeof(PlaceContextMenu), 0.0,
            propertyChanged: OnCoordinatesChanged);

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude
    {
        get => (double)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    /// <summary>
    /// The formatted coordinates text for display.
    /// </summary>
    public static readonly BindableProperty CoordinatesTextProperty =
        BindableProperty.Create(nameof(CoordinatesText), typeof(string), typeof(PlaceContextMenu), string.Empty);

    /// <summary>
    /// Gets or sets the coordinates text.
    /// </summary>
    public string CoordinatesText
    {
        get => (string)GetValue(CoordinatesTextProperty);
        set => SetValue(CoordinatesTextProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when user requests navigation to the location.
    /// </summary>
    public event EventHandler? NavigateToRequested;

    /// <summary>
    /// Fired when user wants to share the location.
    /// </summary>
    public event EventHandler? ShareLocationRequested;

    /// <summary>
    /// Fired when user wants to search Wikipedia for nearby articles.
    /// </summary>
    public event EventHandler? WikiSearchRequested;

    /// <summary>
    /// Fired when user wants to navigate via Google Maps.
    /// </summary>
    public event EventHandler? NavigateGoogleMapsRequested;

    /// <summary>
    /// Fired when user closes the context menu.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Fired when user wants to delete the dropped pin.
    /// </summary>
    public event EventHandler? DeletePinRequested;

    #endregion

    /// <summary>
    /// Creates a new instance of PlaceContextMenu.
    /// </summary>
    public PlaceContextMenu()
    {
        InitializeComponent();
    }

    #region Event Handlers

    private static void OnCoordinatesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PlaceContextMenu menu)
        {
            menu.CoordinatesText = $"{menu.Latitude:F5}, {menu.Longitude:F5}";
        }
    }

    private void OnNavigateToClicked(object? sender, EventArgs e)
    {
        NavigateToRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnShareLocationClicked(object? sender, EventArgs e)
    {
        ShareLocationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWikiSearchClicked(object? sender, EventArgs e)
    {
        WikiSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnNavigateGoogleMapsClicked(object? sender, EventArgs e)
    {
        try
        {
            // Open Google Maps navigation
            var uri = $"https://www.google.com/maps/dir/?api=1&destination={Latitude:F6},{Longitude:F6}&travelmode=driving";
            await Launcher.OpenAsync(new Uri(uri));
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaceContextMenu] Failed to open Google Maps: {ex.Message}");
            NavigateGoogleMapsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeletePinClicked(object? sender, EventArgs e)
    {
        DeletePinRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCopyCoordinatesClicked(object? sender, EventArgs e)
    {
        try
        {
            var coords = $"{Latitude:F6}, {Longitude:F6}";
            await Clipboard.SetTextAsync(coords);

            // Show brief feedback
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Copied", $"Coordinates copied to clipboard:\n{coords}", "OK");
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaceContextMenu] Failed to copy coordinates: {ex.Message}");
        }
    }

    #endregion
}
