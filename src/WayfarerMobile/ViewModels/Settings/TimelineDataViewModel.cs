using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for timeline data management including import, export, and queue operations.
/// </summary>
public partial class TimelineDataViewModel : ObservableObject
{
    private readonly ILocationQueueRepository _locationQueueRepository;
    private readonly TimelineExportService _exportService;
    private readonly TimelineImportService _importService;
    private readonly TimelineDataService _timelineDataService;
    private readonly IToastService _toastService;

    #region Observable Properties

    /// <summary>
    /// Gets or sets the pending queue count.
    /// </summary>
    [ObservableProperty]
    private int _pendingQueueCount;

    /// <summary>
    /// Gets or sets the local timeline entry count.
    /// </summary>
    [ObservableProperty]
    private int _localTimelineCount;

    /// <summary>
    /// Gets or sets whether the queue is being cleared.
    /// </summary>
    [ObservableProperty]
    private bool _isClearingQueue;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TimelineDataViewModel.
    /// </summary>
    /// <param name="locationQueueRepository">The location queue repository.</param>
    /// <param name="exportService">The timeline export service.</param>
    /// <param name="importService">The timeline import service.</param>
    /// <param name="timelineDataService">The timeline data service.</param>
    /// <param name="toastService">The toast service.</param>
    public TimelineDataViewModel(
        ILocationQueueRepository locationQueueRepository,
        TimelineExportService exportService,
        TimelineImportService importService,
        TimelineDataService timelineDataService,
        IToastService toastService)
    {
        _locationQueueRepository = locationQueueRepository;
        _exportService = exportService;
        _importService = importService;
        _timelineDataService = timelineDataService;
        _toastService = toastService;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Refreshes the pending queue count.
    /// </summary>
    [RelayCommand]
    public async Task RefreshQueueCountAsync()
    {
        PendingQueueCount = await _locationQueueRepository.GetPendingCountAsync();
    }

    /// <summary>
    /// Refreshes the local timeline count.
    /// </summary>
    [RelayCommand]
    public async Task RefreshTimelineCountAsync()
    {
        LocalTimelineCount = await _timelineDataService.GetEntryCountAsync();
    }

    /// <summary>
    /// Clears the pending location queue.
    /// </summary>
    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (PendingQueueCount == 0)
        {
            await Shell.Current.DisplayAlertAsync("Queue Empty", "There are no pending locations to clear.", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Clear Queue",
            $"This will delete {PendingQueueCount} pending locations that haven't been synced to the server. This cannot be undone.",
            "Clear",
            "Cancel");

        if (confirm)
        {
            IsClearingQueue = true;
            try
            {
                var deleted = await _locationQueueRepository.ClearPendingQueueAsync();
                PendingQueueCount = 0;
                await Shell.Current.DisplayAlertAsync("Queue Cleared", $"{deleted} pending locations have been deleted.", "OK");
            }
            finally
            {
                IsClearingQueue = false;
            }
        }
    }

    /// <summary>
    /// Exports timeline data to CSV format.
    /// </summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var result = await _exportService.ShareExportAsync("csv");
            if (result != null)
            {
                await _toastService.ShowSuccessAsync("Timeline exported successfully");
            }
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Exports timeline data to GeoJSON format.
    /// </summary>
    [RelayCommand]
    private async Task ExportGeoJsonAsync()
    {
        try
        {
            var result = await _exportService.ShareExportAsync("geojson");
            if (result != null)
            {
                await _toastService.ShowSuccessAsync("Timeline exported successfully");
            }
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Imports timeline data from a file.
    /// </summary>
    [RelayCommand]
    private async Task ImportTimelineAsync()
    {
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/csv", "application/json", "application/geo+json", "*/*" } },
                { DevicePlatform.iOS, new[] { "public.comma-separated-values-text", "public.json" } }
            });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select timeline file to import",
                FileTypes = fileTypes
            });

            if (result == null)
                return;

            using var stream = await result.OpenReadAsync();

            ImportResult importResult;
            if (result.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                importResult = await _importService.ImportFromCsvAsync(stream);
            }
            else if (result.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                     result.FileName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
            {
                importResult = await _importService.ImportFromGeoJsonAsync(stream);
            }
            else
            {
                await _toastService.ShowWarningAsync("Unsupported file format. Use CSV or GeoJSON.");
                return;
            }

            // Show result
            var message = $"Imported: {importResult.Imported}, Updated: {importResult.Updated}, Skipped: {importResult.Skipped}";
            if (importResult.Errors.Any())
            {
                message += $"\nErrors: {importResult.Errors.Count}";
            }

            await Shell.Current.DisplayAlertAsync("Import Complete", message, "OK");

            // Refresh count
            await RefreshTimelineCountAsync();
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Import failed: {ex.Message}");
        }
    }

    #endregion
}
