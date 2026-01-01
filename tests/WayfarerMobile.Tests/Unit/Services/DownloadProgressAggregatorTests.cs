using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for DownloadProgressAggregator.
/// Tests centralized download progress event aggregation.
/// </summary>
/// <remarks>
/// Part of Phase 0 infrastructure for refactoring (Issue #93).
/// </remarks>
public class DownloadProgressAggregatorTests
{
    private readonly DownloadProgressAggregator _aggregator;
    private readonly ILogger<DownloadProgressAggregator> _logger;

    public DownloadProgressAggregatorTests()
    {
        _logger = NullLogger<DownloadProgressAggregator>.Instance;
        _aggregator = new DownloadProgressAggregator(_logger);
    }

    #region PublishProgress Tests

    [Fact]
    public void PublishProgress_TracksActiveDownload()
    {
        // Arrange
        var progress = new DownloadProgressEventArgs
        {
            TripId = 1,
            ProgressPercent = 50,
            StatusMessage = "Downloading..."
        };

        // Act
        _aggregator.PublishProgress(progress);

        // Assert
        var current = _aggregator.GetCurrentProgress(1);
        current.Should().NotBeNull();
        current!.ProgressPercent.Should().Be(50);
    }

    [Fact]
    public void PublishProgress_RaisesEvent()
    {
        // Arrange
        DownloadProgressEventArgs? received = null;
        _aggregator.ProgressChanged += (_, e) => received = e;

        var progress = new DownloadProgressEventArgs
        {
            TripId = 1,
            ProgressPercent = 75,
            StatusMessage = "Almost done"
        };

        // Act
        _aggregator.PublishProgress(progress);

        // Assert
        received.Should().NotBeNull();
        received!.TripId.Should().Be(1);
        received.ProgressPercent.Should().Be(75);
    }

    [Fact]
    public void PublishProgress_UpdatesExistingProgress()
    {
        // Arrange
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 25 });
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 50 });
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 75 });

        // Act
        var current = _aggregator.GetCurrentProgress(1);

        // Assert
        current!.ProgressPercent.Should().Be(75);
    }

    #endregion

    #region GetActiveDownloads Tests

    [Fact]
    public void GetActiveDownloads_ReturnsAllActive()
    {
        // Arrange
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 25 });
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 2, ProgressPercent = 50 });
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 3, ProgressPercent = 75 });

        // Act
        var active = _aggregator.GetActiveDownloads();

        // Assert
        active.Should().HaveCount(3);
        active.Should().ContainKey(1);
        active.Should().ContainKey(2);
        active.Should().ContainKey(3);
    }

    [Fact]
    public void GetCurrentProgress_ReturnsNullForUnknownTrip()
    {
        // Act
        var progress = _aggregator.GetCurrentProgress(999);

        // Assert
        progress.Should().BeNull();
    }

    #endregion

    #region PublishCompleted Tests

    [Fact]
    public void PublishCompleted_RemovesFromActive()
    {
        // Arrange
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 99 });

        // Act
        _aggregator.PublishCompleted(new DownloadTerminalEventArgs
        {
            TripId = 1,
            TripServerId = Guid.NewGuid(),
            TripName = "Test Trip",
            TilesDownloaded = 100,
            TotalBytes = 1000000
        });

        // Assert
        _aggregator.GetCurrentProgress(1).Should().BeNull();
    }

    [Fact]
    public void PublishCompleted_RaisesEvent()
    {
        // Arrange
        DownloadTerminalEventArgs? received = null;
        _aggregator.DownloadCompleted += (_, e) => received = e;

        // Act
        _aggregator.PublishCompleted(new DownloadTerminalEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            TilesDownloaded = 100
        });

        // Assert
        received.Should().NotBeNull();
        received!.TripName.Should().Be("Test Trip");
    }

    #endregion

    #region PublishFailed Tests

    [Fact]
    public void PublishFailed_RemovesFromActive()
    {
        // Arrange
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 50 });

        // Act
        _aggregator.PublishFailed(new DownloadTerminalEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            ErrorMessage = "Network error"
        });

        // Assert
        _aggregator.GetCurrentProgress(1).Should().BeNull();
    }

    [Fact]
    public void PublishFailed_RaisesEvent()
    {
        // Arrange
        DownloadTerminalEventArgs? received = null;
        _aggregator.DownloadFailed += (_, e) => received = e;

        // Act
        _aggregator.PublishFailed(new DownloadTerminalEventArgs
        {
            TripId = 1,
            ErrorMessage = "Disk full"
        });

        // Assert
        received.Should().NotBeNull();
        received!.ErrorMessage.Should().Be("Disk full");
    }

    #endregion

    #region PublishPaused Tests

    [Fact]
    public void PublishPaused_RaisesEvent()
    {
        // Arrange
        DownloadPausedEventArgs? received = null;
        _aggregator.DownloadPaused += (_, e) => received = e;

        // Act
        _aggregator.PublishPaused(new DownloadPausedEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            Reason = DownloadPauseReasonType.UserRequest,
            TilesCompleted = 50,
            TotalTiles = 100
        });

        // Assert
        received.Should().NotBeNull();
        received!.Reason.Should().Be(DownloadPauseReasonType.UserRequest);
    }

    #endregion

    #region PublishCacheLimitWarning Tests

    [Fact]
    public void PublishCacheLimitWarning_RaisesEvent()
    {
        // Arrange
        CacheLimitEventArgs? received = null;
        _aggregator.CacheLimitWarning += (_, e) => received = e;

        // Act
        _aggregator.PublishCacheLimitWarning(new CacheLimitEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            Level = CacheLimitLevel.Warning,
            CurrentUsageMB = 1600,
            MaxSizeMB = 2000,
            UsagePercent = 80
        });

        // Assert
        received.Should().NotBeNull();
        received!.Level.Should().Be(CacheLimitLevel.Warning);
    }

    #endregion

    #region ClearDownload Tests

    [Fact]
    public void ClearDownload_RemovesFromActive()
    {
        // Arrange
        _aggregator.PublishProgress(new DownloadProgressEventArgs { TripId = 1, ProgressPercent = 50 });

        // Act
        _aggregator.ClearDownload(1);

        // Assert
        _aggregator.GetCurrentProgress(1).Should().BeNull();
    }

    [Fact]
    public void ClearDownload_DoesNotThrowForUnknownTrip()
    {
        // Act & Assert
        var act = () => _aggregator.ClearDownload(999);
        act.Should().NotThrow();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentPublish_MaintainsConsistency()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - fire many concurrent updates for different trips
        for (int i = 0; i < 100; i++)
        {
            var tripId = i;
            tasks.Add(Task.Run(() => _aggregator.PublishProgress(
                new DownloadProgressEventArgs { TripId = tripId, ProgressPercent = tripId })));
        }

        await Task.WhenAll(tasks);

        // Assert
        var active = _aggregator.GetActiveDownloads();
        active.Should().HaveCount(100);
    }

    #endregion
}

#region Local Copy of DownloadProgressAggregator (for testing)

/// <summary>
/// Local test copy of DownloadProgressAggregator.
/// Omits MainThread dispatch since tests don't have MAUI dispatcher.
/// </summary>
public class DownloadProgressAggregator : IDownloadProgressAggregator
{
    private readonly ILogger<DownloadProgressAggregator> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DownloadProgressEventArgs> _activeDownloads = new();

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<DownloadTerminalEventArgs>? DownloadCompleted;
    public event EventHandler<DownloadTerminalEventArgs>? DownloadFailed;
    public event EventHandler<DownloadPausedEventArgs>? DownloadPaused;
    public event EventHandler<CacheLimitEventArgs>? CacheLimitWarning;

    public DownloadProgressAggregator(ILogger<DownloadProgressAggregator> logger)
    {
        _logger = logger;
    }

    public DownloadProgressEventArgs? GetCurrentProgress(int tripId)
    {
        return _activeDownloads.TryGetValue(tripId, out var progress) ? progress : null;
    }

    public IReadOnlyDictionary<int, DownloadProgressEventArgs> GetActiveDownloads()
    {
        return _activeDownloads;
    }

    public void PublishProgress(DownloadProgressEventArgs progress)
    {
        _activeDownloads[progress.TripId] = progress;
        ProgressChanged?.Invoke(this, progress);
    }

    public void PublishCompleted(DownloadTerminalEventArgs args)
    {
        _activeDownloads.TryRemove(args.TripId, out _);
        DownloadCompleted?.Invoke(this, args);
    }

    public void PublishFailed(DownloadTerminalEventArgs args)
    {
        _activeDownloads.TryRemove(args.TripId, out _);
        DownloadFailed?.Invoke(this, args);
    }

    public void PublishPaused(DownloadPausedEventArgs args)
    {
        DownloadPaused?.Invoke(this, args);
    }

    public void PublishCacheLimitWarning(CacheLimitEventArgs args)
    {
        CacheLimitWarning?.Invoke(this, args);
    }

    public void ClearDownload(int tripId)
    {
        _activeDownloads.TryRemove(tripId, out _);
    }
}

#endregion
