using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for browsing and cloning public trips.
/// </summary>
public partial class PublicTripsViewModel : BaseViewModel
{
    private readonly IApiClient _apiClient;
    private readonly IToastService _toastService;
    private readonly ILogger<PublicTripsViewModel> _logger;

    private int _currentPage = 1;
    private const int PageSize = 20;
    private bool _hasMorePages = true;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Callback invoked when a trip is successfully cloned.
    /// Set by the parent coordinator (TripsPageViewModel).
    /// </summary>
    public Func<Task>? OnCloneSuccess { get; set; }

    /// <summary>
    /// Gets the collection of public trips.
    /// </summary>
    public ObservableCollection<PublicTripSummary> Trips { get; } = new();

    /// <summary>
    /// Gets or sets the search query.
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// Gets or sets the selected sort option.
    /// </summary>
    [ObservableProperty]
    private string _selectedSort = PublicTripsSortOptions.Updated;

    /// <summary>
    /// Gets or sets whether more trips are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// Gets or sets whether the list is empty.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Gets or sets whether a clone operation is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isCloning;

    /// <summary>
    /// Gets the available sort options.
    /// </summary>
    public string[] SortOptions { get; } = new[]
    {
        PublicTripsSortOptions.Updated,
        PublicTripsSortOptions.Newest,
        PublicTripsSortOptions.Name,
        PublicTripsSortOptions.Places
    };

    /// <summary>
    /// Creates a new instance of PublicTripsViewModel.
    /// </summary>
    /// <param name="apiClient">The API client.</param>
    /// <param name="toastService">The toast service.</param>
    /// <param name="logger">The logger instance.</param>
    public PublicTripsViewModel(
        IApiClient apiClient,
        IToastService toastService,
        ILogger<PublicTripsViewModel> logger)
    {
        _apiClient = apiClient;
        _toastService = toastService;
        _logger = logger;
        Title = "Public Trips";
    }

    /// <summary>
    /// Called when the search query changes (with debounce).
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();

        // Debounce search
        _ = DebounceSearchAsync(value, _searchCts.Token);
    }

    /// <summary>
    /// Debounces search to avoid too many API calls.
    /// </summary>
    private async Task DebounceSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);
            if (!ct.IsCancellationRequested)
            {
                await RefreshAsync();
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when cancelled
        }
    }

    /// <summary>
    /// Called when sort option changes.
    /// </summary>
    partial void OnSelectedSortChanged(string value)
    {
        _ = RefreshAsync();
    }

    /// <summary>
    /// Refreshes the trips list from the beginning.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            _currentPage = 1;
            _hasMorePages = true;
            Trips.Clear();

            await LoadTripsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads more trips (infinite scroll).
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !_hasMorePages || IsBusy)
            return;

        try
        {
            IsLoadingMore = true;
            _currentPage++;
            await LoadTripsAsync();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>
    /// Loads trips from the API.
    /// </summary>
    private async Task LoadTripsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var response = await _apiClient.GetPublicTripsAsync(
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                SelectedSort,
                _currentPage,
                PageSize,
                cts.Token);

            if (response == null)
            {
                _logger.LogWarning("Failed to load public trips");
                return;
            }

            foreach (var trip in response.Trips)
            {
                Trips.Add(trip);
            }

            _hasMorePages = response.HasMore;
            IsEmpty = Trips.Count == 0;

            _logger.LogDebug(
                "Loaded {Count} public trips (page {Page}, hasMore: {HasMore})",
                response.Trips.Count,
                _currentPage,
                _hasMorePages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading public trips");
        }
    }

    /// <summary>
    /// Clones a public trip to the user's collection.
    /// </summary>
    /// <param name="trip">The trip to clone.</param>
    [RelayCommand]
    private async Task CloneTripAsync(PublicTripSummary trip)
    {
        if (IsCloning)
            return;

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Copy Trip",
            $"Copy \"{trip.Name}\" to your trips? This will create a copy that you can edit.",
            "Copy",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            IsCloning = true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _apiClient.CloneTripAsync(trip.Id, cts.Token);

            if (result?.Success == true && result.NewTripId.HasValue)
            {
                _logger.LogInformation("Successfully cloned trip {TripName} as {NewTripId}", trip.Name, result.NewTripId);
                await _toastService.ShowSuccessAsync($"'{trip.Name}' added to your trips");

                // Notify coordinator to refresh My Trips and switch tabs
                if (OnCloneSuccess != null)
                {
                    await OnCloneSuccess();
                }
            }
            else
            {
                var errorMessage = result?.Error ?? "Unknown error occurred";
                _logger.LogWarning("Failed to clone trip: {Error}", errorMessage);
                await _toastService.ShowErrorAsync($"Clone failed: {errorMessage}");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error cloning trip {TripId}: {Message}", trip.Id, ex.Message);
            await _toastService.ShowErrorAsync("Network error. Please check your connection.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Timeout cloning trip {TripId}", trip.Id);
            await _toastService.ShowErrorAsync("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning trip {TripId}", trip.Id);
            await _toastService.ShowErrorAsync("Failed to copy trip. Please try again.");
        }
        finally
        {
            IsCloning = false;
        }
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        if (Trips.Count == 0)
        {
            await RefreshAsync();
        }

        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Cleans up resources.
    /// </summary>
    protected override void Cleanup()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        base.Cleanup();
    }
}
