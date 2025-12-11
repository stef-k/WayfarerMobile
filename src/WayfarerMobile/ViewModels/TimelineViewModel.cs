using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the timeline page showing location history.
/// </summary>
public partial class TimelineViewModel : BaseViewModel
{
    #region Fields

    private readonly DatabaseService _database;
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the collection of timeline items grouped by day.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TimelineGroup> _timelineGroups = new();

    /// <summary>
    /// Gets or sets the selected date for filtering.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDateText))]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// Gets or sets whether data is being refreshed.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Gets or sets whether there are no items.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Gets or sets the total count of locations.
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// Gets or sets the selected entry for the details sheet.
    /// </summary>
    [ObservableProperty]
    private TimelineItem? _selectedEntry;

    /// <summary>
    /// Gets or sets whether the entry details sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isEntryDetailsOpen;

    /// <summary>
    /// Gets or sets whether the date picker popup is open.
    /// </summary>
    [ObservableProperty]
    private bool _isDatePickerOpen;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the formatted selected date text.
    /// </summary>
    public string SelectedDateText
    {
        get
        {
            if (SelectedDate.Date == DateTime.Today)
                return "Today";
            if (SelectedDate.Date == DateTime.Today.AddDays(-1))
                return "Yesterday";
            return SelectedDate.ToString("ddd, MMM d, yyyy");
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TimelineViewModel.
    /// </summary>
    public TimelineViewModel(
        DatabaseService database,
        ITimelineSyncService timelineSyncService,
        IToastService toastService)
    {
        _database = database;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        Title = "Timeline";

        // Subscribe to sync events
        _timelineSyncService.SyncCompleted += OnSyncCompleted;
        _timelineSyncService.SyncQueued += OnSyncQueued;
        _timelineSyncService.SyncRejected += OnSyncRejected;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Loads timeline data from the database.
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            IsRefreshing = true;

            var locations = await _database.GetLocationsForDateAsync(SelectedDate);

            // Group by hour for better organization
            var groups = locations
                .GroupBy(l => l.Timestamp.Hour)
                .OrderByDescending(g => g.Key)
                .Select(g => new TimelineGroup(
                    $"{g.Key:00}:00 - {g.Key:00}:59",
                    g.OrderByDescending(l => l.Timestamp).ToList()))
                .ToList();

            TimelineGroups = new ObservableCollection<TimelineGroup>(groups);
            TotalCount = locations.Count;
            IsEmpty = !groups.Any();
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Refreshes the timeline data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    /// <summary>
    /// Goes to the previous day.
    /// </summary>
    [RelayCommand]
    private async Task PreviousDayAsync()
    {
        SelectedDate = SelectedDate.AddDays(-1);
        await LoadDataAsync();
    }

    /// <summary>
    /// Goes to the next day.
    /// </summary>
    [RelayCommand]
    private async Task NextDayAsync()
    {
        SelectedDate = SelectedDate.AddDays(1);
        await LoadDataAsync();
    }

    /// <summary>
    /// Goes to today.
    /// </summary>
    [RelayCommand]
    private async Task TodayAsync()
    {
        SelectedDate = DateTime.Today;
        await LoadDataAsync();
    }

    /// <summary>
    /// Opens date picker to select a specific date.
    /// </summary>
    [RelayCommand]
    private void OpenDatePicker()
    {
        IsDatePickerOpen = true;
    }

    /// <summary>
    /// Called when the date picker selection changes.
    /// </summary>
    [RelayCommand]
    private async Task DateSelectedAsync(DateTime? date)
    {
        if (date.HasValue && date.Value.Date != SelectedDate.Date)
        {
            SelectedDate = date.Value.Date;
            await LoadDataAsync();
        }
        IsDatePickerOpen = false;
    }

    /// <summary>
    /// Closes the date picker without selecting.
    /// </summary>
    [RelayCommand]
    private void CloseDatePicker()
    {
        IsDatePickerOpen = false;
    }

    /// <summary>
    /// Shows the entry details bottom sheet.
    /// </summary>
    [RelayCommand]
    private void ShowEntryDetails(TimelineItem? entry)
    {
        if (entry == null)
            return;

        SelectedEntry = entry;
        IsEntryDetailsOpen = true;
    }

    /// <summary>
    /// Closes the entry details bottom sheet.
    /// </summary>
    public void CloseEntryDetails()
    {
        IsEntryDetailsOpen = false;
        SelectedEntry = null;
    }

    /// <summary>
    /// Saves entry changes with optimistic UI update.
    /// </summary>
    /// <param name="e">The timeline entry update event args.</param>
    public async Task SaveEntryChangesAsync(TimelineEntryUpdateEventArgs e)
    {
        // Apply optimistic UI update to the local item
        if (SelectedEntry != null)
        {
            SelectedEntry.Location.Latitude = e.Latitude;
            SelectedEntry.Location.Longitude = e.Longitude;
            SelectedEntry.Location.Timestamp = e.LocalTimestamp;
            SelectedEntry.Location.Notes = e.Notes;
        }

        // Sync to server (handles offline queueing automatically)
        await _timelineSyncService.UpdateLocationAsync(
            e.LocationId,
            e.Latitude,
            e.Longitude,
            e.LocalTimestamp,
            e.Notes,
            includeNotes: true);
    }

    /// <summary>
    /// Handles sync completed event.
    /// </summary>
    private async void OnSyncCompleted(object? sender, SyncSuccessEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowSuccessAsync("Changes saved");
        });
    }

    /// <summary>
    /// Handles sync queued event (offline).
    /// </summary>
    private async void OnSyncQueued(object? sender, SyncQueuedEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowWarningAsync(e.Message);
        });
    }

    /// <summary>
    /// Handles sync rejected event (server error).
    /// </summary>
    private async void OnSyncRejected(object? sender, SyncFailureEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowErrorAsync($"Save failed: {e.ErrorMessage}");
        });
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await LoadDataAsync();
        await base.OnAppearingAsync();
    }

    #endregion
}

/// <summary>
/// Represents a group of timeline items (e.g., by hour).
/// </summary>
public class TimelineGroup : List<TimelineItem>
{
    /// <summary>
    /// Gets the group header text.
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Creates a new timeline group.
    /// </summary>
    public TimelineGroup(string header, IEnumerable<QueuedLocation> locations) : base()
    {
        Header = header;
        AddRange(locations.Select(l => new TimelineItem(l)));
    }
}

/// <summary>
/// Represents a single timeline item for display.
/// </summary>
public class TimelineItem
{
    /// <summary>
    /// Gets the underlying location data.
    /// </summary>
    public QueuedLocation Location { get; }

    /// <summary>
    /// Gets the formatted time.
    /// </summary>
    public string TimeText => Location.Timestamp.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// Gets the formatted coordinates.
    /// </summary>
    public string CoordinatesText => $"{Location.Latitude:F6}, {Location.Longitude:F6}";

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => Location.Accuracy.HasValue
        ? $"~{Location.Accuracy.Value:F0}m"
        : "Unknown";

    /// <summary>
    /// Gets the accuracy indicator color.
    /// </summary>
    public Color AccuracyColor => Location.Accuracy switch
    {
        null => Colors.Gray,
        <= 10 => Colors.Green,
        <= 30 => Colors.Orange,
        _ => Colors.Red
    };

    /// <summary>
    /// Gets the sync status icon.
    /// </summary>
    public string SyncStatusIcon => Location.SyncStatus switch
    {
        SyncStatus.Pending => "⏳",
        SyncStatus.Synced => "✓",
        SyncStatus.Failed => "✗",
        _ => "?"
    };

    /// <summary>
    /// Gets the provider text.
    /// </summary>
    public string ProviderText => Location.Provider ?? "Unknown";

    /// <summary>
    /// Gets the speed text if available.
    /// </summary>
    public string? SpeedText => Location.Speed.HasValue
        ? $"{Location.Speed.Value * 3.6:F1} km/h"
        : null;

    /// <summary>
    /// Creates a new timeline item.
    /// </summary>
    public TimelineItem(QueuedLocation location)
    {
        Location = location;
    }
}
