using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>Coordinates provider-independent Trip-data download presentation.</summary>
public partial class TripDownloadViewModel : ObservableObject, IDisposable
{
    private readonly ITripDownloadService _downloads;
    private readonly IToastService _toast;
    private readonly ILogger<TripDownloadViewModel> _logger;
    private ITripDownloadCallbacks? _callbacks;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string? _downloadStatusMessage;
    [ObservableProperty] private string? _downloadingTripName;
    private Guid? _serverId;

    public TripDownloadViewModel(ITripDownloadService downloads, IToastService toast, ILogger<TripDownloadViewModel> logger)
    {
        _downloads = downloads;
        _toast = toast;
        _logger = logger;
        _downloads.ProgressChanged += OnProgressChanged;
    }

    public void SetCallbacks(ITripDownloadCallbacks callbacks) => _callbacks = callbacks;

    [RelayCommand]
    private async Task QuickDownloadAsync(TripListItem? item)
    {
        if (item is null || IsDownloading) return;
        _cancellation = new CancellationTokenSource();
        _serverId = item.ServerId;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadingTripName = item.Name;
        DownloadStatusMessage = "Starting Trip-data download…";
        item.UnifiedState = UnifiedDownloadState.Downloading;
        try
        {
            var summary = item.ServerTrip ?? new TripSummary { Id = item.ServerId, Name = item.Name, BoundingBox = item.BoundingBox };
            var result = await _downloads.DownloadTripAsync(summary, _cancellation.Token);
            if (result is null)
            {
                item.UnifiedState = UnifiedDownloadState.Failed;
                await _toast.ShowErrorAsync("Trip data could not be downloaded");
            }
            else
            {
                item.DownloadedEntity = result;
                item.UnifiedState = UnifiedDownloadState.Downloaded;
                await _toast.ShowSuccessAsync("Trip data downloaded");
            }
            if (_callbacks is not null) await _callbacks.RefreshTripsAsync();
        }
        catch (OperationCanceledException)
        {
            item.UnifiedState = UnifiedDownloadState.ServerOnly;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trip-data download failed");
            item.UnifiedState = UnifiedDownloadState.Failed;
            await _toast.ShowErrorAsync("Trip data could not be downloaded");
        }
        finally
        {
            IsDownloading = false;
            _serverId = null;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task DeleteDownloadAsync(TripListItem? item)
    {
        if (item is null) return;
        await _downloads.DeleteTripAsync(item.ServerId);
        if (_callbacks is not null) await _callbacks.RefreshTripsAsync();
    }

    private void OnProgressChanged(object? sender, Core.Interfaces.DownloadProgressEventArgs e)
    {
        DownloadProgress = e.ProgressPercent / 100d;
        DownloadStatusMessage = e.StatusMessage;
        if (_serverId is { } id) _callbacks?.UpdateItemProgress(id, DownloadProgress, e.ProgressPercent < 100);
    }

    public void Dispose()
    {
        _downloads.ProgressChanged -= OnProgressChanged;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }
}
