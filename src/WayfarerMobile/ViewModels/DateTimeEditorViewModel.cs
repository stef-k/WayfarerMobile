using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for datetime editing functionality in the timeline.
/// Handles datetime picker display and datetime save operations.
/// </summary>
public partial class DateTimeEditorViewModel : ObservableObject
{
    private readonly IDateTimeEditorCallbacks _callbacks;
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private readonly ILogger<DateTimeEditorViewModel> _logger;

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether the edit datetime picker is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private bool _isEditDateTimePickerOpen;

    /// <summary>
    /// Gets or sets the datetime being edited.
    /// </summary>
    [ObservableProperty]
    private DateTime _editDateTime = DateTime.Now;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// </summary>
    public bool IsEditing => IsEditDateTimePickerOpen;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of DateTimeEditorViewModel.
    /// </summary>
    /// <param name="callbacks">The callback interface for parent state access.</param>
    /// <param name="timelineSyncService">The timeline sync service.</param>
    /// <param name="toastService">The toast service.</param>
    /// <param name="logger">The logger.</param>
    public DateTimeEditorViewModel(
        IDateTimeEditorCallbacks callbacks,
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ILogger<DateTimeEditorViewModel> logger)
    {
        _callbacks = callbacks;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        _logger = logger;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Opens the datetime picker for editing the selected location.
    /// </summary>
    [RelayCommand]
    private void OpenEditDateTimePicker()
    {
        var selectedLocation = _callbacks.SelectedLocation;
        if (selectedLocation == null) return;

        // Set the picker to the current location's datetime
        EditDateTime = selectedLocation.LocalTimestamp;
        IsEditDateTimePickerOpen = true;
    }

    /// <summary>
    /// Saves the edited datetime from the picker.
    /// </summary>
    [RelayCommand]
    private async Task SaveEditDateTimeAsync()
    {
        var selectedLocation = _callbacks.SelectedLocation;
        if (selectedLocation == null) return;

        // Store locationId before any changes (reference becomes stale after reload)
        var locationId = selectedLocation.LocationId;

        // Check online status
        if (!_callbacks.IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Changes will sync when online.");
        }

        try
        {
            _callbacks.IsBusy = true;

            // User entered local time - convert to UTC for the server
            // The SfDateTimePicker returns DateTimeKind.Unspecified, but it represents local time
            var utcDateTime = DateTime.SpecifyKind(EditDateTime, DateTimeKind.Local).ToUniversalTime();

            await _timelineSyncService.UpdateLocationAsync(
                locationId,
                latitude: null,
                longitude: null,
                localTimestamp: utcDateTime,
                notes: null,
                includeNotes: false);

            // Close picker
            IsEditDateTimePickerOpen = false;

            // Reload data to reflect changes on map and groupings
            await _callbacks.ReloadTimelineAsync();

            // Re-select the location to show updated details in bottom sheet
            _callbacks.ShowLocationDetails(locationId);
            _callbacks.OpenLocationSheet();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error saving datetime");
            await _toastService.ShowErrorAsync("Network error. Changes will sync when online.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving datetime");
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            _callbacks.IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels datetime editing.
    /// </summary>
    [RelayCommand]
    private void CancelEditDateTime()
    {
        IsEditDateTimePickerOpen = false;
    }

    #endregion
}
