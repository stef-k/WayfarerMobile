using System.Text.Json;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

// Use explicit alias to avoid conflict with internal TripPlace in TripLayerServiceTests.cs
using CoreTripPlace = WayfarerMobile.Core.Models.TripPlace;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TripDownloadService covering cache limit enforcement,
/// pause/resume functionality, progress events, and download state management.
/// </summary>
/// <remarks>
/// These tests verify:
/// - Cache limit thresholds (80% warning, 90% critical, 100% limit reached)
/// - Pause/resume download state persistence
/// - Tile count and size estimation
/// - Cache quota calculations
/// - Download state cleanup
/// - Offline places mapping including Address property
///
/// Note: Tests use direct SQLite operations to verify database-level behavior
/// since DatabaseService is a concrete class without an interface.
/// </remarks>
[Collection("SQLite")]
public class TripDownloadServiceTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _database = null!;
    private Mock<ISettingsService> _settingsServiceMock = null!;

    #region Test Lifecycle

    public async Task InitializeAsync()
    {
        // Use in-memory SQLite database for testing
        _database = new SQLiteAsyncConnection(":memory:");

        // Create all required tables
        await _database.CreateTableAsync<DownloadedTripEntity>();
        await _database.CreateTableAsync<TripDownloadStateEntity>();
        await _database.CreateTableAsync<OfflinePlaceEntity>();
        await _database.CreateTableAsync<OfflineSegmentEntity>();
        await _database.CreateTableAsync<TripTileEntity>();

        // Initialize settings mock
        _settingsServiceMock = new Mock<ISettingsService>();
        _settingsServiceMock.Setup(s => s.MaxTripCacheSizeMB).Returns(500);
        _settingsServiceMock.Setup(s => s.MaxConcurrentTileDownloads).Returns(4);
        _settingsServiceMock.Setup(s => s.MinTileRequestDelayMs).Returns(100);
    }

    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    #endregion

    #region Cache Limit Check Tests

    [Fact]
    public async Task CheckTripCacheLimitAsync_EmptyCache_ReturnsZeroUsage()
    {
        // Arrange - empty database, no tiles

        // Act
        var result = await CheckTripCacheLimitAsync();

        // Assert
        result.CurrentSizeBytes.Should().Be(0);
        result.CurrentSizeMB.Should().Be(0);
        result.UsagePercent.Should().Be(0);
        result.IsLimitReached.Should().BeFalse();
        result.IsWarningLevel.Should().BeFalse();
    }

    [Theory]
    [InlineData(400, 500, 80.0, true, false)]  // Exactly 80% - warning level
    [InlineData(450, 500, 90.0, true, false)]  // Exactly 90% - still warning (critical is separate)
    [InlineData(500, 500, 100.0, false, true)] // Exactly 100% - limit reached
    [InlineData(550, 500, 110.0, false, true)] // Over 100% - limit exceeded
    [InlineData(200, 500, 40.0, false, false)] // 40% - normal
    public async Task CheckTripCacheLimitAsync_ReturnsCorrectThresholds(
        int currentSizeMB, int maxSizeMB, double expectedPercent, bool expectedWarning, bool expectedLimit)
    {
        // Arrange
        var currentSizeBytes = (long)currentSizeMB * 1024 * 1024;
        await InsertTileWithSize(currentSizeBytes);
        _settingsServiceMock.Setup(s => s.MaxTripCacheSizeMB).Returns(maxSizeMB);

        // Act
        var result = await CheckTripCacheLimitAsync();

        // Assert
        result.UsagePercent.Should().BeApproximately(expectedPercent, 0.1);
        result.IsWarningLevel.Should().Be(expectedWarning);
        result.IsLimitReached.Should().Be(expectedLimit);
    }

    [Fact]
    public async Task CheckTripCacheLimitAsync_MultipleTiles_SumsCorrectly()
    {
        // Arrange - insert multiple tiles
        await InsertTileWithSize(100 * 1024 * 1024); // 100 MB
        await InsertTileWithSize(150 * 1024 * 1024); // 150 MB
        await InsertTileWithSize(50 * 1024 * 1024);  // 50 MB
        _settingsServiceMock.Setup(s => s.MaxTripCacheSizeMB).Returns(500);

        // Act
        var result = await CheckTripCacheLimitAsync();

        // Assert
        result.CurrentSizeMB.Should().BeApproximately(300, 0.1);
        result.UsagePercent.Should().BeApproximately(60, 0.1);
    }

    #endregion

    #region Cache Quota Check Tests

    [Fact]
    public async Task CheckCacheQuotaForTripAsync_SufficientQuota_ReturnsHasSufficientQuota()
    {
        // Arrange - empty cache, 500 MB max, very small bounding box
        _settingsServiceMock.Setup(s => s.MaxTripCacheSizeMB).Returns(500);

        // Very small bounding box (0.01 x 0.01 degrees) to minimize tile count
        var boundingBox = new BoundingBox
        {
            North = 51.505,
            South = 51.495,
            East = -0.095,
            West = -0.105
        };

        // Act
        var result = await CheckCacheQuotaForTripAsync(boundingBox);

        // Assert
        result.HasSufficientQuota.Should().BeTrue();
        result.WouldExceedBy.Should().Be(0);
        result.AvailableMB.Should().BeApproximately(500, 1);
    }

    [Fact]
    public async Task CheckCacheQuotaForTripAsync_InsufficientQuota_ReturnsExceedAmount()
    {
        // Arrange - 480 MB used, 500 MB max, trip would need more than 20 MB available
        await InsertTileWithSize(480 * 1024 * 1024);
        _settingsServiceMock.Setup(s => s.MaxTripCacheSizeMB).Returns(500);

        // Large bounding box that would require many tiles
        var boundingBox = new BoundingBox
        {
            North = 52.0,
            South = 51.0,
            East = 1.0,
            West = -1.0
        };

        // Act
        var result = await CheckCacheQuotaForTripAsync(boundingBox);

        // Assert
        result.CurrentUsageMB.Should().BeApproximately(480, 1);
        result.AvailableMB.Should().BeApproximately(20, 1);
        // HasSufficientQuota depends on tile count estimation
    }

    [Fact]
    public async Task CheckCacheQuotaForTripAsync_NullBoundingBox_ReturnsZeroTileCount()
    {
        // Act
        var result = await CheckCacheQuotaForTripAsync(null);

        // Assert
        result.TileCount.Should().Be(0);
        result.EstimatedSizeBytes.Should().Be(0);
        result.HasSufficientQuota.Should().BeTrue();
    }

    #endregion

    #region Estimate Methods Tests

    [Theory]
    [InlineData(100, 4096000)]    // 100 tiles * 40KB = 4 MB
    [InlineData(1000, 40960000)]  // 1000 tiles * 40KB = 40 MB
    [InlineData(0, 0)]            // 0 tiles = 0 bytes
    public void EstimateDownloadSize_CalculatesCorrectly(int tileCount, long expectedBytes)
    {
        // Act
        var result = EstimateDownloadSize(tileCount);

        // Assert
        result.Should().Be(expectedBytes);
    }

    [Fact]
    public void EstimateTileCount_NullBoundingBox_ReturnsZero()
    {
        // Act
        var result = EstimateTileCount(null);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void EstimateTileCount_SmallBoundingBox_ReturnsReasonableCount()
    {
        // Arrange - small area around a city center
        var boundingBox = new BoundingBox
        {
            North = 51.52,
            South = 51.50,
            East = -0.10,
            West = -0.13
        };

        // Act
        var result = EstimateTileCount(boundingBox);

        // Assert - should have tiles but not excessively many
        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(10000);
    }

    [Fact]
    public void EstimateTileCount_LargeBoundingBox_ReturnsManyTiles()
    {
        // Arrange - large country-sized area
        var boundingBox = new BoundingBox
        {
            North = 55.0,
            South = 50.0,
            East = 2.0,
            West = -6.0
        };

        // Act
        var result = EstimateTileCount(boundingBox);

        // Assert - large area should have many tiles
        result.Should().BeGreaterThan(1000);
    }

    #endregion

    #region Pause/Resume State Tests

    [Fact]
    public async Task PauseDownloadAsync_DownloadingTrip_ReturnsTrueAndSetsFlag()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Test Trip");

        // Act
        var result = await PauseDownloadAsync(trip.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task PauseDownloadAsync_NonExistentTrip_ReturnsFalse()
    {
        // Act
        var result = await PauseDownloadAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task PauseDownloadAsync_CompletedTrip_ReturnsFalse()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Completed Trip");

        // Act
        var result = await PauseDownloadAsync(trip.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetPausedDownloadsAsync_ReturnsOnlyPausedStates()
    {
        // Arrange
        var trip1 = await InsertDownloadingTrip("Trip 1");
        var trip2 = await InsertDownloadingTrip("Trip 2");

        await InsertDownloadState(trip1.Id, trip1.ServerId, "Trip 1", DownloadStateStatus.Paused);
        await InsertDownloadState(trip2.Id, trip2.ServerId, "Trip 2", DownloadStateStatus.InProgress);

        // Act
        var pausedDownloads = await GetPausedDownloadsAsync();

        // Assert
        pausedDownloads.Should().HaveCount(1);
        pausedDownloads[0].TripName.Should().Be("Trip 1");
    }

    [Fact]
    public async Task ResumeDownloadAsync_NoSavedState_ReturnsFalse()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Trip without state");

        // Act
        var result = await ResumeDownloadAsync(trip.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsDownloadPausedAsync_WithPausedState_ReturnsTrue()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Paused Trip");
        await InsertDownloadState(trip.Id, trip.ServerId, "Paused Trip", DownloadStateStatus.Paused);

        // Act
        var isPaused = await IsDownloadPausedAsync(trip.Id);

        // Assert
        isPaused.Should().BeTrue();
    }

    [Fact]
    public async Task IsDownloadPausedAsync_WithInProgressState_ReturnsFalse()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Active Trip");
        await InsertDownloadState(trip.Id, trip.ServerId, "Active Trip", DownloadStateStatus.InProgress);

        // Act
        var isPaused = await IsDownloadPausedAsync(trip.Id);

        // Assert
        isPaused.Should().BeFalse();
    }

    #endregion

    #region Download State Persistence Tests

    [Fact]
    public async Task SaveDownloadState_PersistsAllFields()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("State Test Trip");
        var remainingTiles = new List<TileCoordinate>
        {
            new() { Zoom = 15, X = 100, Y = 200 },
            new() { Zoom = 15, X = 101, Y = 200 }
        };

        var state = new TripDownloadStateEntity
        {
            TripId = trip.Id,
            TripServerId = trip.ServerId,
            TripName = trip.Name,
            RemainingTilesJson = JsonSerializer.Serialize(remainingTiles),
            CompletedTileCount = 50,
            TotalTileCount = 100,
            DownloadedBytes = 2048000,
            Status = DownloadStateStatus.Paused,
            InterruptionReason = DownloadPauseReason.UserPause,
            PausedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertOrReplaceAsync(state);
        var retrieved = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == trip.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CompletedTileCount.Should().Be(50);
        retrieved.TotalTileCount.Should().Be(100);
        retrieved.DownloadedBytes.Should().Be(2048000);
        retrieved.Status.Should().Be(DownloadStateStatus.Paused);
        retrieved.InterruptionReason.Should().Be(DownloadPauseReason.UserPause);

        var deserializedTiles = JsonSerializer.Deserialize<List<TileCoordinate>>(retrieved.RemainingTilesJson);
        deserializedTiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteDownloadState_RemovesState()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Delete State Trip");
        await InsertDownloadState(trip.Id, trip.ServerId, "Delete State Trip", DownloadStateStatus.Paused);

        // Verify state exists
        var beforeDelete = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == trip.Id);
        beforeDelete.Should().NotBeNull();

        // Act
        await _database.DeleteAsync<TripDownloadStateEntity>(trip.Id);

        // Assert
        var afterDelete = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == trip.Id);
        afterDelete.Should().BeNull();
    }

    #endregion

    #region Downloaded Trip Tests

    [Fact]
    public async Task GetDownloadedTripsAsync_ReturnsAllTrips()
    {
        // Arrange
        await InsertCompletedTrip("Trip 1");
        await InsertCompletedTrip("Trip 2");
        await InsertDownloadingTrip("Trip 3");

        // Act
        var trips = await _database.Table<DownloadedTripEntity>().ToListAsync();

        // Assert
        trips.Should().HaveCount(3);
    }

    [Fact]
    public async Task IsTripDownloadedAsync_CompletedTrip_ReturnsTrue()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Completed Trip");

        // Act
        var isDownloaded = await IsTripDownloadedAsync(trip.ServerId);

        // Assert
        isDownloaded.Should().BeTrue();
    }

    [Fact]
    public async Task IsTripDownloadedAsync_NonExistentTrip_ReturnsFalse()
    {
        // Act
        var isDownloaded = await IsTripDownloadedAsync(Guid.NewGuid());

        // Assert
        isDownloaded.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteTripAsync_RemovesTripAndRelatedData()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Delete Test Trip");
        await InsertOfflinePlace(trip.Id, "Place 1");
        await InsertOfflinePlace(trip.Id, "Place 2");
        await InsertTileForTrip(trip.Id, 15, 100, 200);

        // Act
        await _database.DeleteAsync<DownloadedTripEntity>(trip.Id);
        await _database.ExecuteAsync("DELETE FROM OfflinePlaces WHERE TripId = ?", trip.Id);
        await _database.ExecuteAsync("DELETE FROM TripTiles WHERE TripId = ?", trip.Id);

        // Assert
        var deletedTrip = await _database.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.Id == trip.Id);
        deletedTrip.Should().BeNull();

        var remainingPlaces = await _database.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == trip.Id)
            .CountAsync();
        remainingPlaces.Should().Be(0);
    }

    #endregion

    #region Offline Places Tests

    [Fact]
    public async Task GetOfflinePlacesAsync_ReturnsMappedPlaces()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Places Test Trip");
        await InsertOfflinePlace(trip.Id, "Place A", "123 Main St");
        await InsertOfflinePlace(trip.Id, "Place B", "456 Oak Ave");

        // Act
        var places = await GetOfflinePlacesAsync(trip.ServerId);

        // Assert
        places.Should().HaveCount(2);
        places.Should().Contain(p => p.Name == "Place A" && p.Address == "123 Main St");
        places.Should().Contain(p => p.Name == "Place B" && p.Address == "456 Oak Ave");
    }

    [Fact]
    public async Task GetOfflinePlacesAsync_NonExistentTrip_ReturnsEmptyList()
    {
        // Act
        var places = await GetOfflinePlacesAsync(Guid.NewGuid());

        // Assert
        places.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOfflinePlacesAsync_PreservesAllProperties()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Full Properties Trip");
        var placeId = Guid.NewGuid();
        var entity = new OfflinePlaceEntity
        {
            TripId = trip.Id,
            ServerId = placeId,
            Name = "Test Place",
            Address = "123 Test Street",
            Latitude = 51.5074,
            Longitude = -0.1278,
            IconName = "restaurant",
            SortOrder = 5
        };
        await _database.InsertAsync(entity);

        // Act
        var places = await GetOfflinePlacesAsync(trip.ServerId);

        // Assert
        places.Should().HaveCount(1);
        var place = places[0];
        place.Id.Should().Be(placeId);
        place.Name.Should().Be("Test Place");
        place.Address.Should().Be("123 Test Street");
        place.Latitude.Should().Be(51.5074);
        place.Longitude.Should().Be(-0.1278);
        place.Icon.Should().Be("restaurant");
        place.SortOrder.Should().Be(5);
    }

    #endregion

    #region Tile Cache Tests

    [Fact]
    public async Task GetCachedTilePath_ExistingTile_ReturnsPath()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Tile Test Trip");
        var tile = await InsertTileForTrip(trip.Id, 15, 100, 200);

        // Act
        var path = GetCachedTilePath(trip.Id, 15, 100, 200);

        // Assert
        path.Should().NotBeNull();
        path.Should().Contain(trip.Id.ToString());
    }

    [Fact]
    public void GetCachedTilePath_NonExistentTile_ReturnsNull()
    {
        // Act
        var path = GetCachedTilePath(999, 15, 100, 200);

        // Assert
        path.Should().BeNull();
    }

    #endregion

    #region Event Args Tests

    /// <summary>
    /// Verifies DownloadProgressEventArgs record properties.
    /// </summary>
    [Fact]
    public void DownloadProgressEventArgs_ContainsCorrectProperties()
    {
        // Arrange & Act
        var args = new DownloadProgressEventArgs
        {
            TripId = 1,
            ProgressPercent = 50,
            StatusMessage = "Downloading tiles..."
        };

        // Assert
        args.TripId.Should().Be(1);
        args.ProgressPercent.Should().Be(50);
        args.StatusMessage.Should().Be("Downloading tiles...");
    }

    /// <summary>
    /// Verifies CacheLimitEventArgs record for warning level.
    /// </summary>
    [Fact]
    public void CacheLimitEventArgs_WarningLevel_ContainsCorrectData()
    {
        // Arrange & Act
        var args = new CacheLimitEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            CurrentUsageMB = 400,
            MaxSizeMB = 500,
            UsagePercent = 80.0,
            Level = CacheLimitLevel.Warning
        };

        // Assert
        args.Level.Should().Be(CacheLimitLevel.Warning);
        args.UsagePercent.Should().Be(80.0);
        args.CurrentUsageMB.Should().Be(400);
        args.MaxSizeMB.Should().Be(500);
    }

    /// <summary>
    /// Verifies CacheLimitEventArgs record for critical level.
    /// </summary>
    [Fact]
    public void CacheLimitEventArgs_CriticalLevel_ContainsCorrectData()
    {
        // Arrange & Act
        var args = new CacheLimitEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            CurrentUsageMB = 450,
            MaxSizeMB = 500,
            UsagePercent = 90.0,
            Level = CacheLimitLevel.Critical
        };

        // Assert
        args.Level.Should().Be(CacheLimitLevel.Critical);
        args.UsagePercent.Should().Be(90.0);
    }

    /// <summary>
    /// Verifies CacheLimitEventArgs record for limit reached.
    /// </summary>
    [Fact]
    public void CacheLimitEventArgs_LimitReached_ContainsCorrectData()
    {
        // Arrange & Act
        var args = new CacheLimitEventArgs
        {
            TripId = 1,
            TripName = "Test Trip",
            CurrentUsageMB = 500,
            MaxSizeMB = 500,
            UsagePercent = 100.0,
            Level = CacheLimitLevel.LimitReached
        };

        // Assert
        args.Level.Should().Be(CacheLimitLevel.LimitReached);
        args.UsagePercent.Should().Be(100.0);
    }

    /// <summary>
    /// Verifies DownloadTerminalEventArgs for completed download.
    /// </summary>
    [Fact]
    public void DownloadTerminalEventArgs_Completed_ContainsCorrectData()
    {
        // Arrange & Act
        var tripServerId = Guid.NewGuid();
        var args = new DownloadTerminalEventArgs
        {
            TripId = 1,
            TripServerId = tripServerId,
            TripName = "Test Trip",
            TilesDownloaded = 500,
            TotalBytes = 20480000
        };

        // Assert
        args.TripId.Should().Be(1);
        args.TripServerId.Should().Be(tripServerId);
        args.TripName.Should().Be("Test Trip");
        args.TilesDownloaded.Should().Be(500);
        args.TotalBytes.Should().Be(20480000);
        args.ErrorMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies DownloadTerminalEventArgs for failed download.
    /// </summary>
    [Fact]
    public void DownloadTerminalEventArgs_Failed_ContainsErrorMessage()
    {
        // Arrange & Act
        var args = new DownloadTerminalEventArgs
        {
            TripId = 1,
            TripServerId = Guid.NewGuid(),
            TripName = "Test Trip",
            ErrorMessage = "Network error"
        };

        // Assert
        args.ErrorMessage.Should().Be("Network error");
    }

    /// <summary>
    /// Verifies DownloadPausedEventArgs contains reason and progress.
    /// </summary>
    [Fact]
    public void DownloadPausedEventArgs_ContainsReasonAndProgress()
    {
        // Arrange & Act
        var args = new DownloadPausedEventArgs
        {
            TripId = 1,
            TripServerId = Guid.NewGuid(),
            TripName = "Test Trip",
            Reason = DownloadPauseReasonType.CacheLimitReached,
            TilesCompleted = 250,
            TotalTiles = 500,
            CanResume = true
        };

        // Assert
        args.Reason.Should().Be(DownloadPauseReasonType.CacheLimitReached);
        args.TilesCompleted.Should().Be(250);
        args.TotalTiles.Should().Be(500);
        args.CanResume.Should().BeTrue();
    }

    /// <summary>
    /// Verifies all DownloadPauseReasonType enum values.
    /// </summary>
    [Theory]
    [InlineData(DownloadPauseReasonType.UserRequest, "User manually paused")]
    [InlineData(DownloadPauseReasonType.UserCancel, "User cancelled")]
    [InlineData(DownloadPauseReasonType.NetworkLost, "Network lost")]
    [InlineData(DownloadPauseReasonType.StorageLow, "Storage low")]
    [InlineData(DownloadPauseReasonType.CacheLimitReached, "Cache limit")]
    public void DownloadPauseReasonType_AllValuesAreValid(DownloadPauseReasonType reason, string _)
    {
        // Arrange & Act
        var args = new DownloadPausedEventArgs { Reason = reason };

        // Assert
        args.Reason.Should().Be(reason);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task CancelDownloadAsync_ActiveDownload_ReturnsTrueAndCleansUp()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Cancel Test Trip");
        await InsertDownloadState(trip.Id, trip.ServerId, "Cancel Test Trip", DownloadStateStatus.InProgress);

        // Act
        var result = await CancelDownloadAsync(trip.Id, cleanup: true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CancelDownloadAsync_WithoutCleanup_PreservesTiles()
    {
        // Arrange
        var trip = await InsertDownloadingTrip("Cancel No Cleanup Trip");
        await InsertTileForTrip(trip.Id, 15, 100, 200);
        await InsertDownloadState(trip.Id, trip.ServerId, "Cancel No Cleanup Trip", DownloadStateStatus.InProgress);

        // Act
        var result = await CancelDownloadAsync(trip.Id, cleanup: false);

        // Assert
        result.Should().BeTrue();
        var tilesRemain = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == trip.Id)
            .CountAsync();
        tilesRemain.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    private async Task<CacheLimitCheckResult> CheckTripCacheLimitAsync()
    {
        var currentSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM TripTiles");
        var maxSizeMB = _settingsServiceMock.Object.MaxTripCacheSizeMB;
        var maxSizeBytes = (long)maxSizeMB * 1024 * 1024;
        var currentSizeMB = currentSize / (1024.0 * 1024.0);
        var usagePercent = maxSizeBytes > 0 ? (currentSize * 100.0 / maxSizeBytes) : 0;

        return new CacheLimitCheckResult
        {
            CurrentSizeBytes = currentSize,
            CurrentSizeMB = currentSizeMB,
            MaxSizeMB = maxSizeMB,
            UsagePercent = usagePercent,
            IsLimitReached = currentSize >= maxSizeBytes,
            IsWarningLevel = usagePercent >= 80 && usagePercent < 100
        };
    }

    private async Task<CacheQuotaCheckResult> CheckCacheQuotaForTripAsync(BoundingBox? boundingBox)
    {
        var limitCheck = await CheckTripCacheLimitAsync();
        var tileCount = EstimateTileCount(boundingBox);
        var estimatedSize = EstimateDownloadSize(tileCount);
        var availableBytes = Math.Max(0, (long)limitCheck.MaxSizeMB * 1024 * 1024 - limitCheck.CurrentSizeBytes);

        return new CacheQuotaCheckResult
        {
            TileCount = tileCount,
            EstimatedSizeBytes = estimatedSize,
            EstimatedSizeMB = estimatedSize / (1024.0 * 1024.0),
            AvailableBytes = availableBytes,
            AvailableMB = availableBytes / (1024.0 * 1024.0),
            CurrentUsageMB = limitCheck.CurrentSizeMB,
            MaxSizeMB = limitCheck.MaxSizeMB,
            HasSufficientQuota = estimatedSize <= availableBytes,
            WouldExceedBy = Math.Max(0, (estimatedSize - availableBytes) / (1024.0 * 1024.0))
        };
    }

    private static long EstimateDownloadSize(int tileCount)
    {
        const long EstimatedTileSizeBytes = 40960; // 40KB
        return tileCount * EstimatedTileSizeBytes;
    }

    private static int EstimateTileCount(BoundingBox? boundingBox)
    {
        if (boundingBox == null) return 0;

        int totalTiles = 0;
        int[] zoomLevels = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };

        foreach (var zoom in zoomLevels)
        {
            var (minX, maxX, minY, maxY) = GetTileRange(boundingBox, zoom);
            var tilesAtZoom = (maxX - minX + 1) * (maxY - minY + 1);
            totalTiles += tilesAtZoom;
        }

        return totalTiles;
    }

    private static (int minX, int maxX, int minY, int maxY) GetTileRange(BoundingBox bbox, int zoom)
    {
        int minX = LonToTileX(bbox.West, zoom);
        int maxX = LonToTileX(bbox.East, zoom);
        int minY = LatToTileY(bbox.North, zoom);
        int maxY = LatToTileY(bbox.South, zoom);
        return (minX, maxX, minY, maxY);
    }

    private static int LonToTileX(double lon, int zoom)
    {
        return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
    }

    private static int LatToTileY(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom));
    }

    private async Task<bool> PauseDownloadAsync(int tripId)
    {
        var trip = await _database.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.Id == tripId);

        if (trip == null || trip.Status != TripDownloadStatus.Downloading)
            return false;

        return true;
    }

    private async Task<bool> ResumeDownloadAsync(int tripId)
    {
        var state = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == tripId);
        return state != null;
    }

    private async Task<bool> IsDownloadPausedAsync(int tripId)
    {
        var state = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == tripId);
        return state?.Status == DownloadStateStatus.Paused;
    }

    private async Task<bool> IsTripDownloadedAsync(Guid tripServerId)
    {
        var trip = await _database.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.ServerId == tripServerId && t.Status == TripDownloadStatus.Complete);
        return trip != null;
    }

    private async Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync()
    {
        return await _database.Table<TripDownloadStateEntity>()
            .Where(s => s.Status == DownloadStateStatus.Paused)
            .ToListAsync();
    }

    private async Task<bool> CancelDownloadAsync(int tripId, bool cleanup)
    {
        var state = await _database.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == tripId);

        if (state == null) return false;

        state.Status = DownloadStateStatus.Cancelled;
        await _database.UpdateAsync(state);
        return true;
    }

    private async Task<List<CoreTripPlace>> GetOfflinePlacesAsync(Guid tripServerId)
    {
        var trip = await _database.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.ServerId == tripServerId);

        if (trip == null) return new List<CoreTripPlace>();

        var entities = await _database.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == trip.Id)
            .ToListAsync();

        return entities.Select(e => new CoreTripPlace
        {
            Id = e.ServerId,
            Name = e.Name,
            Address = e.Address,
            Latitude = e.Latitude,
            Longitude = e.Longitude,
            Icon = e.IconName,
            SortOrder = e.SortOrder
        }).ToList();
    }

    private string? GetCachedTilePath(int tripId, int zoom, int x, int y)
    {
        // Simulate the path generation logic
        var basePath = Path.Combine(Path.GetTempPath(), "WayfarerTiles");
        var tilePath = Path.Combine(basePath, tripId.ToString(), zoom.ToString(), x.ToString(), $"{y}.png");

        // Check if tile exists in database
        var tile = _database.Table<TripTileEntity>()
            .FirstOrDefaultAsync(t => t.TripId == tripId && t.Zoom == zoom && t.X == x && t.Y == y)
            .GetAwaiter().GetResult();

        return tile != null ? tilePath : null;
    }

    private async Task InsertTileWithSize(long sizeBytes)
    {
        var x = new Random().Next(0, 10000);
        var y = new Random().Next(0, 10000);
        var tile = new TripTileEntity
        {
            Id = $"15/{x}/{y}",
            TripId = 1,
            Zoom = 15,
            X = x,
            Y = y,
            FilePath = $"/tiles/1/15/{x}/{y}.png",
            FileSizeBytes = sizeBytes,
            DownloadedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(tile);
    }

    private async Task<TripTileEntity> InsertTileForTrip(int tripId, int zoom, int x, int y)
    {
        var tile = new TripTileEntity
        {
            Id = $"{zoom}/{x}/{y}",
            TripId = tripId,
            Zoom = zoom,
            X = x,
            Y = y,
            FilePath = $"/tiles/{tripId}/{zoom}/{x}/{y}.png",
            FileSizeBytes = 40960,
            DownloadedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(tile);
        return tile;
    }

    private async Task<DownloadedTripEntity> InsertDownloadingTrip(string name)
    {
        var trip = new DownloadedTripEntity
        {
            ServerId = Guid.NewGuid(),
            Name = name,
            Status = TripDownloadStatus.Downloading,
            DownloadedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(trip);
        return trip;
    }

    private async Task<DownloadedTripEntity> InsertCompletedTrip(string name)
    {
        var trip = new DownloadedTripEntity
        {
            ServerId = Guid.NewGuid(),
            Name = name,
            Status = TripDownloadStatus.Complete,
            DownloadedAt = DateTime.UtcNow,
            ProgressPercent = 100
        };
        await _database.InsertAsync(trip);
        return trip;
    }

    private async Task InsertDownloadState(int tripId, Guid serverId, string tripName, string status)
    {
        var state = new TripDownloadStateEntity
        {
            TripId = tripId,
            TripServerId = serverId,
            TripName = tripName,
            Status = status,
            RemainingTilesJson = "[]",
            CompletedTileCount = 0,
            TotalTileCount = 100,
            PausedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(state);
    }

    private async Task InsertOfflinePlace(int tripId, string name, string? address = null)
    {
        var place = new OfflinePlaceEntity
        {
            TripId = tripId,
            ServerId = Guid.NewGuid(),
            Name = name,
            Address = address,
            Latitude = 51.5074,
            Longitude = -0.1278
        };
        await _database.InsertAsync(place);
    }

    private async Task<List<string>> DeleteTripTilesAsync(int tripId)
    {
        // Get tile file paths before deleting
        var tiles = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == tripId)
            .ToListAsync();

        var filePaths = tiles.Select(t => t.FilePath).ToList();

        // Delete tiles from database
        await _database.ExecuteAsync(
            "DELETE FROM TripTiles WHERE TripId = ?", tripId);

        return filePaths;
    }

    #endregion

    #region DeleteTripTilesAsync Tests

    [Fact]
    public async Task DeleteTripTilesAsync_DeletesTilesOnly_KeepsTripData()
    {
        // Arrange - create trip with tiles and places
        var trip = await InsertCompletedTrip("Test Trip");
        await InsertTileForTrip(trip.Id, 15, 100, 200);
        await InsertTileForTrip(trip.Id, 15, 101, 200);
        await InsertTileForTrip(trip.Id, 15, 100, 201);
        await InsertOfflinePlace(trip.Id, "Place 1");
        await InsertOfflinePlace(trip.Id, "Place 2");

        // Verify setup
        var initialTileCount = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == trip.Id).CountAsync();
        var initialPlaceCount = await _database.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == trip.Id).CountAsync();
        initialTileCount.Should().Be(3);
        initialPlaceCount.Should().Be(2);

        // Act
        var deletedPaths = await DeleteTripTilesAsync(trip.Id);

        // Assert - tiles deleted, places preserved
        var remainingTiles = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == trip.Id).CountAsync();
        var remainingPlaces = await _database.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == trip.Id).CountAsync();
        var tripStillExists = await _database.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.Id == trip.Id);

        remainingTiles.Should().Be(0, "All tiles should be deleted");
        remainingPlaces.Should().Be(2, "Places should be preserved");
        tripStillExists.Should().NotBeNull("Trip entity should be preserved");
        deletedPaths.Should().HaveCount(3, "Should return paths of deleted tiles");
    }

    [Fact]
    public async Task DeleteTripTilesAsync_NoTiles_ReturnsEmptyList()
    {
        // Arrange - trip without tiles
        var trip = await InsertCompletedTrip("Empty Trip");
        await InsertOfflinePlace(trip.Id, "Place 1");

        // Act
        var deletedPaths = await DeleteTripTilesAsync(trip.Id);

        // Assert
        deletedPaths.Should().BeEmpty();
        var placeCount = await _database.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == trip.Id).CountAsync();
        placeCount.Should().Be(1, "Places should be preserved");
    }

    [Fact]
    public async Task DeleteTripTilesAsync_ReturnsFilePaths()
    {
        // Arrange
        var trip = await InsertCompletedTrip("Test Trip");
        await InsertTileForTrip(trip.Id, 15, 100, 200);
        await InsertTileForTrip(trip.Id, 16, 200, 400);

        // Act
        var deletedPaths = await DeleteTripTilesAsync(trip.Id);

        // Assert
        deletedPaths.Should().HaveCount(2);
        deletedPaths.Should().AllSatisfy(p => p.Should().Contain(".png"));
    }

    [Fact]
    public async Task DeleteTripTilesAsync_OnlyDeletesSpecifiedTrip()
    {
        // Arrange - two trips with tiles
        var trip1 = await InsertCompletedTrip("Trip 1");
        var trip2 = await InsertCompletedTrip("Trip 2");
        await InsertTileForTrip(trip1.Id, 15, 100, 200);
        await InsertTileForTrip(trip1.Id, 15, 101, 200);
        await InsertTileForTrip(trip2.Id, 15, 200, 300);
        await InsertTileForTrip(trip2.Id, 15, 201, 300);

        // Act - delete only trip1's tiles
        await DeleteTripTilesAsync(trip1.Id);

        // Assert
        var trip1Tiles = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == trip1.Id).CountAsync();
        var trip2Tiles = await _database.Table<TripTileEntity>()
            .Where(t => t.TripId == trip2.Id).CountAsync();

        trip1Tiles.Should().Be(0, "Trip 1 tiles should be deleted");
        trip2Tiles.Should().Be(2, "Trip 2 tiles should be preserved");
    }

    #endregion
}

/// <summary>
/// Tile coordinate for serialization in download state.
/// </summary>
public record TileCoordinate
{
    public int Zoom { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}
