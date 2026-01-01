using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Coordinator ViewModel for the Trips page.
/// Manages tab switching, settings validation, error display, and child ViewModels.
/// </summary>
public partial class TripsPageViewModel : BaseViewModel
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TripsPageViewModel> _logger;

    #region Child ViewModels

    /// <summary>
    /// Gets the My Trips ViewModel.
    /// </summary>
    public MyTripsViewModel MyTrips { get; }

    /// <summary>
    /// Gets the Public Trips ViewModel.
    /// </summary>
    public PublicTripsViewModel PublicTrips { get; }

    /// <summary>
    /// Gets the download ViewModel (for download overlay).
    /// Exposed from MyTripsViewModel for TripsPage.xaml overlay binding.
    /// </summary>
    public TripDownloadViewModel Download => MyTrips.Download;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the selected tab index (0 = My Trips, 1 = Public Trips).
    /// </summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether API is configured.
    /// </summary>
    public bool IsConfigured => _settingsService.IsConfigured;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripsPageViewModel.
    /// </summary>
    public TripsPageViewModel(
        ISettingsService settingsService,
        MyTripsViewModel myTripsViewModel,
        PublicTripsViewModel publicTripsViewModel,
        ILogger<TripsPageViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        Title = "Trips";

        MyTrips = myTripsViewModel;
        PublicTrips = publicTripsViewModel;

        // Subscribe to child ViewModel errors to surface them
        MyTrips.PropertyChanged += OnMyTripsPropertyChanged;

        // Set up clone success callback
        PublicTrips.OnCloneSuccess = OnCloneSuccessAsync;
    }

    #endregion

    #region Property Change Handlers

    /// <summary>
    /// Called when selected tab changes.
    /// </summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        _logger.LogDebug("Tab changed to {TabIndex}", value);

        // Trigger load on Public Trips tab if needed
        if (value == 1 && PublicTrips.Trips.Count == 0 && !PublicTrips.IsBusy)
        {
            _ = PublicTrips.OnAppearingAsync();
        }
    }

    /// <summary>
    /// Forwards relevant errors from MyTripsViewModel.
    /// </summary>
    private void OnMyTripsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MyTripsViewModel.ErrorMessage))
        {
            ErrorMessage = MyTrips.ErrorMessage;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Navigate to settings page.
    /// </summary>
    [RelayCommand]
    private async Task GoToSettingsAsync()
    {
        await Shell.Current.GoToAsync("//settings");
    }

    /// <summary>
    /// Dismiss error message.
    /// </summary>
    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
        MyTrips.ClearError();
    }

    #endregion

    #region Callbacks

    /// <summary>
    /// Called when a public trip is successfully cloned.
    /// Refreshes My Trips and switches to that tab.
    /// </summary>
    private async Task OnCloneSuccessAsync()
    {
        _logger.LogDebug("Clone success callback - refreshing My Trips");

        // Refresh My Trips to show the new clone
        await MyTrips.LoadTripsCommand.ExecuteAsync(null);

        // Switch to My Trips tab
        SelectedTabIndex = 0;
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        _logger.LogDebug("OnAppearingAsync");

        // Delegate to the appropriate child ViewModel based on selected tab
        if (SelectedTabIndex == 0)
        {
            await MyTrips.OnAppearingAsync();
        }
        else
        {
            await PublicTrips.OnAppearingAsync();
        }

        await base.OnAppearingAsync();
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        MyTrips.PropertyChanged -= OnMyTripsPropertyChanged;
        MyTrips.Dispose();
        PublicTrips.Dispose();
        base.Cleanup();
    }

    #endregion
}
