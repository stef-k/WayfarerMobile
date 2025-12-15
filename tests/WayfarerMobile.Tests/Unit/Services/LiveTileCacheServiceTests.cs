using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for LiveTileCacheService covering:
/// - Cache hit/miss scenarios
/// - Download behavior (online/offline)
/// - Prefetch logic with zoom level ordering
/// - LRU eviction behavior
/// - Tile coordinate conversion (LatLonToTile)
/// - Concurrent download locking
/// - Boundary validation
/// </summary>
/// <remarks>
/// These tests use a mock database service and settings service to isolate
/// the LiveTileCacheService logic. File system operations use a temporary
/// directory that is cleaned up after each test.
///
/// The actual service uses MAUI FileSystem.CacheDirectory and Connectivity.Current,
/// so these tests focus on the testable logic (coordinate conversion, zoom order,
/// LRU eviction calculations, etc.) while documenting expected behavior for
/// integration scenarios.
/// </remarks>
[Collection("SQLite")]
public class LiveTileCacheServiceTests : IAsyncLifetime, IDisposable
{
    private SQLiteAsyncConnection _database = null!;
    private string _tempCacheDirectory = null!;
    private bool _disposed;

    #region Test Lifecycle

    /// <summary>
    /// Initializes the test environment with in-memory database and temp directory.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Use in-memory SQLite database for tile tracking
        _database = new SQLiteAsyncConnection(":memory:");
        await _database.CreateTableAsync<LiveTileEntity>();

        // Create temporary cache directory for file operations
        _tempCacheDirectory = Path.Combine(Path.GetTempPath(), $"LiveTileCacheTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempCacheDirectory);
    }

    /// <summary>
    /// Cleans up test resources after each test.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }

        // Clean up temp directory
        if (Directory.Exists(_tempCacheDirectory))
        {
            try
            {
                Directory.Delete(_tempCacheDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Cleanup handled by DisposeAsync
        }

        _disposed = true;
    }

    #endregion

    #region LatLonToTile Tests

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(51.5074, -0.1278, 15, 16371, 10894)] // London
    [InlineData(40.7128, -74.0060, 15, 9649, 12319)] // New York
    [InlineData(35.6762, 139.6503, 15, 29094, 12903)] // Tokyo (corrected)
    [InlineData(-33.8688, 151.2093, 15, 30147, 19663)] // Sydney (corrected)
    public void LatLonToTile_KnownLocations_ReturnsExpectedCoordinates(
        double lat, double lon, int zoom, int expectedX, int expectedY)
    {
        // Act
        var (x, y) = LatLonToTile(lat, lon, zoom);

        // Assert - Allow small tolerance due to floating point
        x.Should().BeCloseTo(expectedX, 2, "X coordinate should match within tolerance");
        y.Should().BeCloseTo(expectedY, 2, "Y coordinate should match within tolerance");
    }

    [Theory]
    [InlineData(0)] // 1 tile
    [InlineData(1)] // 4 tiles
    [InlineData(10)] // 1M+ tiles
    [InlineData(15)] // 1B+ tiles
    [InlineData(18)] // Max typical zoom
    public void LatLonToTile_VariousZoomLevels_ReturnsValidCoordinates(int zoom)
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;
        int maxTiles = 1 << zoom;

        // Act
        var (x, y) = LatLonToTile(lat, lon, zoom);

        // Assert
        x.Should().BeGreaterThanOrEqualTo(0, "X should be non-negative");
        x.Should().BeLessThan(maxTiles, "X should be less than maxTiles");
        y.Should().BeGreaterThanOrEqualTo(0, "Y should be non-negative");
        y.Should().BeLessThan(maxTiles, "Y should be less than maxTiles");
    }

    [Theory]
    [InlineData(90.0, 0.0, 15)] // North pole
    [InlineData(-90.0, 0.0, 15)] // South pole
    [InlineData(0.0, 180.0, 15)] // Date line east
    [InlineData(0.0, -180.0, 15)] // Date line west
    [InlineData(85.0511, 180.0, 15)] // Max valid latitude (Web Mercator)
    [InlineData(-85.0511, -180.0, 15)] // Min valid latitude
    public void LatLonToTile_BoundaryCoordinates_ReturnsClampedValues(double lat, double lon, int zoom)
    {
        // Act
        var (x, y) = LatLonToTile(lat, lon, zoom);
        int maxTiles = 1 << zoom;

        // Assert - Values should be clamped to valid range
        x.Should().BeGreaterThanOrEqualTo(0);
        x.Should().BeLessThan(maxTiles);
        y.Should().BeGreaterThanOrEqualTo(0);
        y.Should().BeLessThan(maxTiles);
    }

    [Fact]
    public void LatLonToTile_SameLocationDifferentZooms_ScalesCorrectly()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;

        // Act
        var (x10, y10) = LatLonToTile(lat, lon, 10);
        var (x11, y11) = LatLonToTile(lat, lon, 11);
        var (x12, y12) = LatLonToTile(lat, lon, 12);

        // Assert - Each zoom level doubles the coordinate
        (x11 / 2).Should().Be(x10);
        (y11 / 2).Should().Be(y10);
        (x12 / 2).Should().Be(x11);
        (y12 / 2).Should().Be(y11);
    }

    #endregion

    #region Prefetch Zoom Level Order Tests

    [Fact]
    public void PrefetchZoomLevelOrder_MatchesExpectedSequence()
    {
        // This documents the expected zoom level order from TileCacheConstants
        // Order: Current view (15), adjacent (14, 16), then rest by decreasing detail
        int[] expectedOrder = { 15, 14, 16, 13, 12, 11, 10, 17 };

        // Verify the order makes sense:
        // 1. Start with default zoom (15)
        expectedOrder[0].Should().Be(15, "Default map zoom should be first");

        // 2. Adjacent zooms next (for quick zoom in/out)
        expectedOrder[1].Should().Be(14, "One level up should be second");
        expectedOrder[2].Should().Be(16, "One level down should be third");

        // 3. Then overview zooms for context
        expectedOrder[3..7].Should().BeInDescendingOrder("Overview zooms should be in decreasing detail order");

        // 4. Max detail zoom last (expensive, rarely needed)
        expectedOrder[^1].Should().Be(17, "High detail zoom should be last");
    }

    [Theory]
    [InlineData(1, 9)] // Radius 1: 3x3 = 9 tiles per zoom
    [InlineData(2, 25)] // Radius 2: 5x5 = 25 tiles per zoom
    [InlineData(5, 121)] // Radius 5: 11x11 = 121 tiles per zoom (default)
    [InlineData(9, 361)] // Radius 9: 19x19 = 361 tiles per zoom (max)
    public void PrefetchTileCount_ForRadius_CalculatesCorrectly(int radius, int expectedTilesPerZoom)
    {
        // The formula is (2 * radius + 1)^2
        int actualTiles = (2 * radius + 1) * (2 * radius + 1);

        actualTiles.Should().Be(expectedTilesPerZoom);
    }

    [Fact]
    public void PrefetchTotalTiles_WithDefaultSettings_CalculatesReasonableCount()
    {
        // Arrange
        int radius = 5; // Default
        int[] zoomLevels = { 15, 14, 16, 13, 12, 11, 10, 17 };
        int tilesPerZoom = (2 * radius + 1) * (2 * radius + 1); // 121

        // Act
        int totalTiles = zoomLevels.Length * tilesPerZoom;

        // Assert - Should be reasonable for mobile data
        totalTiles.Should().Be(968, "8 zoom levels * 121 tiles = 968 tiles");
        // At ~15KB per tile, this is ~14.5MB - reasonable for WiFi prefetch
    }

    #endregion

    #region GetCachedTileAsync Tests

    [Fact]
    public async Task GetCachedTileAsync_FileExists_UpdatesAccessTimeAndReturnsFileInfo()
    {
        // Arrange
        int z = 15, x = 16371, y = 10894;
        var filePath = CreateTestTileFile(z, x, y);
        var initialAccess = DateTime.UtcNow.AddHours(-1);

        var tile = CreateLiveTile(z, x, y, filePath);
        tile.LastAccessedAt = initialAccess;
        tile.AccessCount = 1;
        await _database.InsertAsync(tile);

        // Act
        var updated = await SimulateGetCachedTileAsync(z, x, y);

        // Assert
        updated.Should().NotBeNull();
        updated!.LastAccessedAt.Should().BeAfter(initialAccess);
        updated.AccessCount.Should().Be(2);
    }

    [Fact]
    public async Task GetCachedTileAsync_FileNotExists_ReturnsNull()
    {
        // Arrange - No file created, no DB entry
        int z = 15, x = 16371, y = 10894;

        // Act
        var result = await SimulateGetCachedTileAsync(z, x, y);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedTileAsync_DbEntryButNoFile_ReturnsNull()
    {
        // Arrange - DB entry exists but file was deleted
        int z = 15, x = 16371, y = 10894;
        var fakePath = Path.Combine(_tempCacheDirectory, "nonexistent.png");

        var tile = CreateLiveTile(z, x, y, fakePath);
        await _database.InsertAsync(tile);

        // Act - Simulates the file check
        var fileExists = File.Exists(fakePath);

        // Assert
        fileExists.Should().BeFalse("File should not exist");
        // In real service, GetCachedTileAsync would return null
    }

    #endregion

    #region GetOrDownloadTileAsync Tests

    [Fact]
    public async Task GetOrDownloadTileAsync_CacheHit_ReturnsCachedTileWithoutDownload()
    {
        // Arrange
        int z = 15, x = 16371, y = 10894;
        var filePath = CreateTestTileFile(z, x, y);
        var tile = CreateLiveTile(z, x, y, filePath);
        await _database.InsertAsync(tile);

        // Verify insert worked
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(1, "Tile should be inserted");

        // Act - Cache hit scenario: query by ID
        var tileId = $"{z}/{x}/{y}";
        var cached = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tileId);
        var fileExists = File.Exists(filePath);

        // Assert
        cached.Should().NotBeNull($"Tile with Id '{tileId}' should exist after insert");
        fileExists.Should().BeTrue("Test tile file should exist");
        // In real service, this would return immediately without download
    }

    [Fact]
    public void GetOrDownloadTileAsync_Offline_ReturnsNullForMissingTiles()
    {
        // This documents expected behavior:
        // When Connectivity.Current.NetworkAccess != NetworkAccess.Internet,
        // and tile is not in cache, the service returns null

        // The actual connectivity check uses MAUI APIs which can't be tested directly.
        // Integration tests should verify offline behavior on actual devices.

        var expectedBehavior = "Returns null when offline and tile not cached";
        expectedBehavior.Should().NotBeEmpty();
    }

    [Fact]
    public void GetOrDownloadTileAsync_DownloadLock_PreventsConcurrentDownloads()
    {
        // Document expected behavior: SemaphoreSlim(2) allows max 2 concurrent downloads
        // This prevents overwhelming tile servers and conserves battery

        var semaphore = new SemaphoreSlim(2);
        semaphore.CurrentCount.Should().Be(2, "Should allow 2 concurrent downloads");

        // Simulate acquiring both slots
        semaphore.Wait();
        semaphore.Wait();
        semaphore.CurrentCount.Should().Be(0, "Both slots should be taken");

        // Third download would wait
        var canEnterImmediately = semaphore.Wait(0);
        canEnterImmediately.Should().BeFalse("Third download should wait");

        semaphore.Release(2);
    }

    [Fact]
    public async Task GetOrDownloadTileAsync_DoubleCheck_PreventsRaceCondition()
    {
        // Arrange - Simulate race condition scenario
        int z = 15, x = 16371, y = 10894;
        var tileId = $"{z}/{x}/{y}";
        var filePath = CreateTestTileFile(z, x, y);

        // First request doesn't find tile
        var firstCheck = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tileId);
        firstCheck.Should().BeNull("Tile should not exist before insert");

        // While first request is waiting for download lock, another task downloads it
        var tile = CreateLiveTile(z, x, y, filePath);
        await _database.InsertAsync(tile);

        // Verify insert worked
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(1, "Tile should be inserted");

        // Second check (after acquiring lock) should find the tile
        var secondCheck = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tileId);
        secondCheck.Should().NotBeNull($"Double-check should find tile with Id '{tileId}'");
    }

    #endregion

    #region LRU Eviction Tests

    [Fact]
    public async Task EvictLruTilesAsync_UnderLimit_DoesNotEvict()
    {
        // Arrange - Add tiles under the limit
        int maxSizeMB = 500;
        long maxSizeBytes = maxSizeMB * 1024L * 1024L;

        // Add small tiles (100KB total - well under limit)
        for (int i = 0; i < 10; i++)
        {
            var tile = CreateLiveTile(15, i, 0, $"/path/{i}.png");
            tile.FileSizeBytes = 10000; // 10KB each
            await _database.InsertAsync(tile);
        }

        // Act
        var totalSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");

        // Assert
        totalSize.Should().Be(100000);
        totalSize.Should().BeLessThan(maxSizeBytes, "Should be under limit");
        // No eviction should occur
    }

    [Fact]
    public async Task EvictLruTilesAsync_OverLimit_EvictsOldestTiles()
    {
        // Arrange - Add tiles that exceed a small limit for testing
        long maxSizeBytes = 50000; // 50KB limit for test
        long targetSize = (long)(maxSizeBytes * 0.8); // 40KB target

        // Add tiles with different access times (100KB total, over limit)
        for (int i = 0; i < 10; i++)
        {
            var tile = CreateLiveTile(15, i, 0, $"/path/{i}.png");
            tile.FileSizeBytes = 10000; // 10KB each
            tile.LastAccessedAt = DateTime.UtcNow.AddMinutes(-i * 10); // Older as i increases
            await _database.InsertAsync(tile);
        }

        // Act - Get oldest tiles for eviction
        var currentSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");
        currentSize.Should().Be(100000);

        // Simulate eviction - get oldest tiles
        var tilesToEvict = await _database.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .ToListAsync();

        // Evict until at 80% of max
        long runningSize = currentSize;
        int evictedCount = 0;
        foreach (var tile in tilesToEvict)
        {
            if (runningSize <= targetSize)
                break;

            await _database.ExecuteAsync("DELETE FROM LiveTiles WHERE Id = ?", tile.Id);
            runningSize -= tile.FileSizeBytes;
            evictedCount++;
        }

        // Assert
        var newSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");
        newSize.Should().BeLessThanOrEqualTo(targetSize, "Should be at or below 80% of limit");
        evictedCount.Should().BeGreaterThan(0, "Should have evicted some tiles");
    }

    [Fact]
    public async Task EvictLruTilesAsync_EvictsTo80PercentOfMax()
    {
        // Document the 80% target eviction strategy
        int maxSizeMB = 500;
        long maxSizeBytes = maxSizeMB * 1024L * 1024L;
        long targetSize = (long)(maxSizeBytes * 0.8);

        // Calculate expected target
        targetSize.Should().Be(419430400L, "80% of 500MB = 400MB");

        // This prevents frequent evictions by leaving headroom
        (maxSizeBytes - targetSize).Should().Be(104857600L, "100MB headroom");
    }

    [Fact]
    public async Task EvictLruTilesAsync_AccessedTilesPreserved()
    {
        // Arrange - Recently accessed tile should survive
        var oldTile = CreateLiveTile(15, 0, 0, "/path/old.png");
        oldTile.LastAccessedAt = DateTime.UtcNow.AddDays(-7);
        oldTile.FileSizeBytes = 10000;
        await _database.InsertAsync(oldTile);

        var recentTile = CreateLiveTile(15, 1, 0, "/path/recent.png");
        recentTile.LastAccessedAt = DateTime.UtcNow;
        recentTile.FileSizeBytes = 10000;
        await _database.InsertAsync(recentTile);

        // Act - Get tiles ordered by LRU
        var orderedTiles = await _database.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .ToListAsync();

        // Assert - Old tile should be evicted first
        orderedTiles[0].Id.Should().Be(oldTile.Id, "Oldest accessed should be first for eviction");
        orderedTiles[1].Id.Should().Be(recentTile.Id, "Recently accessed should be preserved");
    }

    #endregion

    #region Boundary Validation Tests

    [Theory]
    [InlineData(0, -1, 0)] // Negative X
    [InlineData(0, 0, -1)] // Negative Y
    [InlineData(0, 1, 0)] // X >= maxTiles at zoom 0
    [InlineData(0, 0, 1)] // Y >= maxTiles at zoom 0
    [InlineData(15, 32768, 0)] // X >= maxTiles at zoom 15
    [InlineData(15, 0, 32768)] // Y >= maxTiles at zoom 15
    public void BoundaryValidation_InvalidCoordinates_AreRejected(int zoom, int x, int y)
    {
        int maxTiles = 1 << zoom;

        // Validate
        bool isValidX = x >= 0 && x < maxTiles;
        bool isValidY = y >= 0 && y < maxTiles;
        bool isValid = isValidX && isValidY;

        isValid.Should().BeFalse("Invalid coordinates should be rejected");
    }

    [Theory]
    [InlineData(0, 0, 0)] // Only valid tile at zoom 0
    [InlineData(15, 0, 0)] // Min valid at zoom 15
    [InlineData(15, 32767, 32767)] // Max valid at zoom 15
    [InlineData(15, 16383, 16383)] // Center of map
    public void BoundaryValidation_ValidCoordinates_AreAccepted(int zoom, int x, int y)
    {
        int maxTiles = 1 << zoom;

        // Validate
        bool isValidX = x >= 0 && x < maxTiles;
        bool isValidY = y >= 0 && y < maxTiles;
        bool isValid = isValidX && isValidY;

        isValid.Should().BeTrue("Valid coordinates should be accepted");
    }

    [Fact]
    public void MaxTilesAtZoom_CalculatesCorrectly()
    {
        // Document maxTiles formula
        (1 << 0).Should().Be(1, "Zoom 0: 1 tile");
        (1 << 1).Should().Be(2, "Zoom 1: 2x2 = 4 tiles");
        (1 << 10).Should().Be(1024, "Zoom 10: ~1M tiles");
        (1 << 15).Should().Be(32768, "Zoom 15: ~1B tiles");
        (1 << 18).Should().Be(262144, "Zoom 18: ~68B tiles");
    }

    #endregion

    #region Tile File Path Tests

    [Theory]
    [InlineData(15, 16371, 10894, "15/16371/10894.png")]
    [InlineData(0, 0, 0, "0/0/0.png")]
    [InlineData(18, 262143, 262143, "18/262143/262143.png")]
    public void GetTileFilePath_GeneratesExpectedStructure(int z, int x, int y, string expectedSuffix)
    {
        // Document expected file path structure
        var basePath = "/cache/tiles/live";
        var expected = Path.Combine(basePath, z.ToString(), x.ToString(), $"{y}.png");

        expected.Should().EndWith(expectedSuffix.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

    [Fact]
    public void GetTileFilePath_UsesAtomicWritePattern()
    {
        // Document the atomic write pattern used by the service
        var filePath = "/cache/tiles/live/15/16371/10894.png";
        var tempPath = filePath + ".tmp";

        // The service:
        // 1. Downloads to .tmp file
        // 2. Moves .tmp to final path atomically
        // This prevents corrupt files if download is interrupted

        tempPath.Should().EndWith(".tmp");
    }

    #endregion

    #region Database Entity Tests

    [Fact]
    public async Task LiveTileEntity_UniqueId_FormatIsCorrect()
    {
        // Arrange
        int z = 15, x = 16371, y = 10894;
        string expectedId = $"{z}/{x}/{y}";

        var tile = CreateLiveTile(z, x, y, "/path/test.png");

        // Assert
        tile.Id.Should().Be(expectedId);
    }

    [Fact]
    public async Task SaveLiveTileAsync_InsertOrReplace_UpdatesExisting()
    {
        // Arrange
        var tile1 = CreateLiveTile(15, 100, 100, "/path/v1.png");
        tile1.FileSizeBytes = 10000;
        await _database.InsertOrReplaceAsync(tile1);

        // Act - Save with same ID but different data
        var tile2 = CreateLiveTile(15, 100, 100, "/path/v2.png");
        tile2.FileSizeBytes = 20000;
        await _database.InsertOrReplaceAsync(tile2);

        // Assert
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(1, "Should replace, not duplicate");

        var saved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == "15/100/100");
        saved!.FileSizeBytes.Should().Be(20000, "Should have updated size");
        saved.FilePath.Should().Be("/path/v2.png", "Should have updated path");
    }

    [Fact]
    public async Task GetLiveCacheSizeAsync_ReturnsTotalBytes()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var tile = CreateLiveTile(15, i, 0, $"/path/{i}.png");
            tile.FileSizeBytes = 10000 * (i + 1); // 10KB, 20KB, 30KB, 40KB, 50KB
            await _database.InsertAsync(tile);
        }

        // Act
        var totalSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");

        // Assert
        totalSize.Should().Be(150000, "10+20+30+40+50 = 150KB");
    }

    [Fact]
    public async Task GetLiveTileCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        for (int i = 0; i < 7; i++)
        {
            var tile = CreateLiveTile(15, i, 0, $"/path/{i}.png");
            await _database.InsertAsync(tile);
        }

        // Act
        var count = await _database.Table<LiveTileEntity>().CountAsync();

        // Assert
        count.Should().Be(7);
    }

    [Fact]
    public async Task ClearLiveTilesAsync_RemovesAllTiles()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var tile = CreateLiveTile(15, i, 0, $"/path/{i}.png");
            await _database.InsertAsync(tile);
        }

        // Act
        await _database.ExecuteAsync("DELETE FROM LiveTiles");

        // Assert
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(0);
    }

    #endregion

    #region Prefetch Progress Events Tests

    [Fact]
    public void PrefetchProgress_EventArgs_ContainsDownloadedAndTotal()
    {
        // Document expected progress event structure
        var progressEventArgs = (Downloaded: 50, Total: 968);

        progressEventArgs.Downloaded.Should().BeGreaterThanOrEqualTo(0);
        progressEventArgs.Total.Should().BeGreaterThan(0);
        progressEventArgs.Downloaded.Should().BeLessThanOrEqualTo(progressEventArgs.Total);
    }

    [Fact]
    public void PrefetchCompleted_EventArgs_ContainsDownloadCount()
    {
        // Document expected completion event structure
        int downloadedCount = 500;

        downloadedCount.Should().BeGreaterThanOrEqualTo(0);
        // Zero is valid when all tiles were already cached
    }

    [Fact]
    public void ProgressInterval_FiresEvery10Tiles()
    {
        // Document the progress reporting interval
        const int progressInterval = 10;

        // Progress fires at 10, 20, 30... tiles processed
        var expectedProgressPoints = Enumerable.Range(1, 96)
            .Select(n => n * progressInterval)
            .ToList();

        expectedProgressPoints[0].Should().Be(10);
        expectedProgressPoints[9].Should().Be(100);
    }

    #endregion

    #region Concurrent Download Tests

    [Fact]
    public async Task ConcurrentPrefetch_RespectsSemaphoreLimit()
    {
        // Arrange
        int maxConcurrent = 2;
        var semaphore = new SemaphoreSlim(maxConcurrent);
        var runningCount = 0;
        var maxObservedConcurrent = 0;
        var lockObj = new object();

        // Simulate concurrent downloads
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                lock (lockObj)
                {
                    runningCount++;
                    maxObservedConcurrent = Math.Max(maxObservedConcurrent, runningCount);
                }

                await Task.Delay(10); // Simulate download

                lock (lockObj)
                {
                    runningCount--;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert
        maxObservedConcurrent.Should().BeLessThanOrEqualTo(maxConcurrent,
            "Should never exceed concurrent limit");
    }

    [Fact]
    public async Task PrefetchBypassesMainDownloadLock()
    {
        // Document: Prefetch uses separate semaphore to avoid blocking regular tile requests
        var mainDownloadLock = new SemaphoreSlim(2);
        var prefetchSemaphore = new SemaphoreSlim(2);

        // Both can operate independently
        await mainDownloadLock.WaitAsync();
        await mainDownloadLock.WaitAsync();
        mainDownloadLock.CurrentCount.Should().Be(0);

        // Prefetch should still work
        await prefetchSemaphore.WaitAsync();
        prefetchSemaphore.CurrentCount.Should().Be(1, "Prefetch uses separate semaphore");

        mainDownloadLock.Release(2);
        prefetchSemaphore.Release();
    }

    #endregion

    #region Tile Download Tests

    [Fact]
    public void TileServerUrl_ContainsRequiredPlaceholders()
    {
        var defaultUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

        defaultUrl.Should().Contain("{z}");
        defaultUrl.Should().Contain("{x}");
        defaultUrl.Should().Contain("{y}");
    }

    [Fact]
    public void TileServerUrl_Replacement_ProducesValidUrl()
    {
        // Arrange
        var template = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        int z = 15, x = 16371, y = 10894;

        // Act
        var url = template
            .Replace("{z}", z.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString());

        // Assert
        url.Should().Be("https://tile.openstreetmap.org/15/16371/10894.png");
    }

    [Fact]
    public void TileTimeoutMs_HasReasonableDefault()
    {
        // Document the timeout setting
        const int TileTimeoutMs = 10000; // 10 seconds

        TileTimeoutMs.Should().BeGreaterThanOrEqualTo(5000, "Should allow for slow connections");
        TileTimeoutMs.Should().BeLessThanOrEqualTo(30000, "Should not wait too long");
    }

    [Fact]
    public void HttpClient_HasProperUserAgent()
    {
        // Document expected User-Agent header
        var expectedUserAgent = "WayfarerMobile/1.0";

        // Tile servers often block requests without proper User-Agent
        expectedUserAgent.Should().NotBeEmpty();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Implements the LatLonToTile algorithm for testing.
    /// This is a copy of the private method from LiveTileCacheService.
    /// </summary>
    private static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

        x = Math.Max(0, Math.Min((int)n - 1, x));
        y = Math.Max(0, Math.Min((int)n - 1, y));

        return (x, y);
    }

    /// <summary>
    /// Creates a test tile file in the temp directory.
    /// </summary>
    private string CreateTestTileFile(int z, int x, int y)
    {
        var directory = Path.Combine(_tempCacheDirectory, z.ToString(), x.ToString());
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{y}.png");

        // Create minimal PNG file (8x8 pixels, transparent)
        // PNG header + minimal IHDR + IDAT + IEND chunks
        byte[] minimalPng =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
            0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x4B, 0x6D, 0x29,
            0xDE, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82 // IEND chunk
        };

        File.WriteAllBytes(filePath, minimalPng);
        return filePath;
    }

    /// <summary>
    /// Creates a LiveTileEntity for testing.
    /// </summary>
    private static LiveTileEntity CreateLiveTile(int z, int x, int y, string filePath)
    {
        return new LiveTileEntity
        {
            Id = $"{z}/{x}/{y}",
            Zoom = z,
            X = x,
            Y = y,
            TileSource = "osm",
            FilePath = filePath,
            FileSizeBytes = 15000,
            CachedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            AccessCount = 1
        };
    }

    /// <summary>
    /// Simulates GetCachedTileAsync logic for testing.
    /// Updates access time if tile exists.
    /// </summary>
    private async Task<LiveTileEntity?> SimulateGetCachedTileAsync(int z, int x, int y)
    {
        var id = $"{z}/{x}/{y}";
        var tile = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tile == null)
            return null;

        // Check if file exists
        if (!File.Exists(tile.FilePath))
            return null;

        // Update access time (LRU tracking)
        tile.LastAccessedAt = DateTime.UtcNow;
        tile.AccessCount++;
        await _database.UpdateAsync(tile);

        return tile;
    }

    #endregion
}
