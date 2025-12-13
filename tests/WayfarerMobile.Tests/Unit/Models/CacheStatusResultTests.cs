using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for cache status result DTOs.
/// </summary>
public class CacheStatusResultTests
{
    #region CacheStatusSummaryResult Tests

    [Fact]
    public void CacheStatusSummaryResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new CacheStatusSummaryResult();

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.Unknown);
        result.CoveragePercent.Should().Be(0.0);
        result.HasNetwork.Should().BeFalse();
        result.TooltipText.Should().Be("Cache");
    }

    [Fact]
    public void CacheStatusSummaryResult_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var result = new CacheStatusSummaryResult
        {
            Status = CacheCoverageStatus.Excellent,
            CoveragePercent = 0.95,
            HasNetwork = true,
            TooltipText = "95% cached"
        };

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.Excellent);
        result.CoveragePercent.Should().Be(0.95);
        result.HasNetwork.Should().BeTrue();
        result.TooltipText.Should().Be("95% cached");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void CacheStatusSummaryResult_CoveragePercent_AcceptsValidRange(double percent)
    {
        // Arrange & Act
        var result = new CacheStatusSummaryResult { CoveragePercent = percent };

        // Assert
        result.CoveragePercent.Should().Be(percent);
    }

    [Theory]
    [InlineData(CacheCoverageStatus.None)]
    [InlineData(CacheCoverageStatus.Poor)]
    [InlineData(CacheCoverageStatus.Partial)]
    [InlineData(CacheCoverageStatus.Good)]
    [InlineData(CacheCoverageStatus.Excellent)]
    [InlineData(CacheCoverageStatus.Error)]
    public void CacheStatusSummaryResult_Status_AcceptsAllEnumValues(CacheCoverageStatus status)
    {
        // Arrange & Act
        var result = new CacheStatusSummaryResult { Status = status };

        // Assert
        result.Status.Should().Be(status);
    }

    #endregion

    #region DetailedCacheStatusResult Tests

    [Fact]
    public void DetailedCacheStatusResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new DetailedCacheStatusResult();

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.Unknown);
        result.CoveragePercent.Should().Be(0.0);
        result.TotalTiles.Should().Be(0);
        result.CachedTiles.Should().Be(0);
        result.LiveCachedTiles.Should().Be(0);
        result.TripCachedTiles.Should().Be(0);
        result.LocalSizeBytes.Should().Be(0);
        result.LiveCacheSizeBytes.Should().Be(0);
        result.TripCacheSizeBytes.Should().Be(0);
        result.TotalAppSizeBytes.Should().Be(0);
        result.LiveTileCount.Should().Be(0);
        result.ActiveTripName.Should().BeNull();
        result.DownloadedTripCount.Should().Be(0);
        result.ZoomCoverage.Should().NotBeNull();
        result.ZoomCoverage.Should().BeEmpty();
        result.CheckedAt.Should().Be(default);
        result.HasNetwork.Should().BeFalse();
    }

    [Fact]
    public void DetailedCacheStatusResult_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var checkedAt = DateTime.UtcNow;
        var zoomCoverage = new List<ZoomCoverageResult>
        {
            new() { Zoom = 14, TotalTiles = 49, CachedTiles = 40, CoveragePercent = 0.82 }
        };

        // Act
        var result = new DetailedCacheStatusResult
        {
            Status = CacheCoverageStatus.Good,
            CoveragePercent = 0.82,
            TotalTiles = 294,
            CachedTiles = 241,
            LiveCachedTiles = 150,
            TripCachedTiles = 91,
            LocalSizeBytes = 2_500_000,
            LiveCacheSizeBytes = 10_500_000,
            TripCacheSizeBytes = 50_000_000,
            TotalAppSizeBytes = 60_500_000,
            LiveTileCount = 5000,
            ActiveTripName = "European Summer 2025",
            DownloadedTripCount = 3,
            ZoomCoverage = zoomCoverage,
            CheckedAt = checkedAt,
            HasNetwork = true
        };

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.Good);
        result.CoveragePercent.Should().Be(0.82);
        result.TotalTiles.Should().Be(294);
        result.CachedTiles.Should().Be(241);
        result.LiveCachedTiles.Should().Be(150);
        result.TripCachedTiles.Should().Be(91);
        result.LocalSizeBytes.Should().Be(2_500_000);
        result.LiveCacheSizeBytes.Should().Be(10_500_000);
        result.TripCacheSizeBytes.Should().Be(50_000_000);
        result.TotalAppSizeBytes.Should().Be(60_500_000);
        result.LiveTileCount.Should().Be(5000);
        result.ActiveTripName.Should().Be("European Summer 2025");
        result.DownloadedTripCount.Should().Be(3);
        result.ZoomCoverage.Should().HaveCount(1);
        result.CheckedAt.Should().Be(checkedAt);
        result.HasNetwork.Should().BeTrue();
    }

    [Fact]
    public void DetailedCacheStatusResult_TileCounts_CanRepresentLargeValues()
    {
        // Arrange - test with large realistic values
        var result = new DetailedCacheStatusResult
        {
            TotalTiles = 1_000_000,
            CachedTiles = 999_999,
            LiveTileCount = 50_000
        };

        // Assert
        result.TotalTiles.Should().Be(1_000_000);
        result.CachedTiles.Should().Be(999_999);
        result.LiveTileCount.Should().Be(50_000);
    }

    [Fact]
    public void DetailedCacheStatusResult_SizeBytes_CanRepresentLargeValues()
    {
        // Arrange - test with large realistic values (several GB)
        var result = new DetailedCacheStatusResult
        {
            LocalSizeBytes = 500_000_000,        // 500 MB
            LiveCacheSizeBytes = 1_073_741_824,  // 1 GB
            TripCacheSizeBytes = 5_368_709_120,  // 5 GB
            TotalAppSizeBytes = 6_442_450_944    // ~6 GB
        };

        // Assert
        result.LocalSizeBytes.Should().Be(500_000_000);
        result.LiveCacheSizeBytes.Should().Be(1_073_741_824);
        result.TripCacheSizeBytes.Should().Be(5_368_709_120);
        result.TotalAppSizeBytes.Should().Be(6_442_450_944);
    }

    [Fact]
    public void DetailedCacheStatusResult_ZoomCoverage_CanBeModified()
    {
        // Arrange
        var result = new DetailedCacheStatusResult();

        // Act
        result.ZoomCoverage.Add(new ZoomCoverageResult { Zoom = 12 });
        result.ZoomCoverage.Add(new ZoomCoverageResult { Zoom = 14 });
        result.ZoomCoverage.Add(new ZoomCoverageResult { Zoom = 16 });

        // Assert
        result.ZoomCoverage.Should().HaveCount(3);
        result.ZoomCoverage.Select(z => z.Zoom).Should().BeEquivalentTo([12, 14, 16]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("My Trip")]
    [InlineData("European Summer 2025")]
    public void DetailedCacheStatusResult_ActiveTripName_AcceptsAllValues(string? tripName)
    {
        // Arrange & Act
        var result = new DetailedCacheStatusResult { ActiveTripName = tripName };

        // Assert
        result.ActiveTripName.Should().Be(tripName);
    }

    #endregion

    #region ZoomCoverageResult Tests

    [Fact]
    public void ZoomCoverageResult_DefaultValues_AreZero()
    {
        // Arrange & Act
        var result = new ZoomCoverageResult();

        // Assert
        result.Zoom.Should().Be(0);
        result.TotalTiles.Should().Be(0);
        result.CachedTiles.Should().Be(0);
        result.LiveTiles.Should().Be(0);
        result.TripTiles.Should().Be(0);
        result.CoveragePercent.Should().Be(0.0);
    }

    [Fact]
    public void ZoomCoverageResult_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var result = new ZoomCoverageResult
        {
            Zoom = 14,
            TotalTiles = 49,
            CachedTiles = 45,
            LiveTiles = 30,
            TripTiles = 15,
            CoveragePercent = 0.918
        };

        // Assert
        result.Zoom.Should().Be(14);
        result.TotalTiles.Should().Be(49);
        result.CachedTiles.Should().Be(45);
        result.LiveTiles.Should().Be(30);
        result.TripTiles.Should().Be(15);
        result.CoveragePercent.Should().BeApproximately(0.918, 0.001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(17)]
    [InlineData(20)]
    public void ZoomCoverageResult_Zoom_AcceptsValidLevels(int zoom)
    {
        // Arrange & Act
        var result = new ZoomCoverageResult { Zoom = zoom };

        // Assert
        result.Zoom.Should().Be(zoom);
    }

    [Fact]
    public void ZoomCoverageResult_CachedTiles_ShouldNotExceedTotalTiles()
    {
        // This is a logical constraint that the service should enforce
        // Here we're just testing that the DTO accepts the values

        // Arrange
        var result = new ZoomCoverageResult
        {
            TotalTiles = 49,
            CachedTiles = 49
        };

        // Assert
        result.CachedTiles.Should().BeLessThanOrEqualTo(result.TotalTiles);
    }

    [Fact]
    public void ZoomCoverageResult_LiveAndTripTiles_ShouldSumToCachedTiles()
    {
        // This is a logical constraint - Live + Trip = Cached
        // Here we're testing the expected relationship

        // Arrange
        var result = new ZoomCoverageResult
        {
            TotalTiles = 49,
            CachedTiles = 45,
            LiveTiles = 30,
            TripTiles = 15
        };

        // Assert
        (result.LiveTiles + result.TripTiles).Should().Be(result.CachedTiles);
    }

    [Theory]
    [InlineData(49, 49, 1.0)]
    [InlineData(49, 0, 0.0)]
    [InlineData(100, 50, 0.5)]
    [InlineData(49, 45, 0.918)]
    public void ZoomCoverageResult_CoveragePercent_MatchesTileRatio(int total, int cached, double expectedPercent)
    {
        // Arrange
        var calculatedPercent = total > 0 ? (double)cached / total : 0.0;

        // Act
        var result = new ZoomCoverageResult
        {
            TotalTiles = total,
            CachedTiles = cached,
            CoveragePercent = calculatedPercent
        };

        // Assert
        result.CoveragePercent.Should().BeApproximately(expectedPercent, 0.01);
    }

    #endregion

    #region Multi-Zoom Coverage Scenario Tests

    [Fact]
    public void DetailedCacheStatusResult_MultipleZoomLevels_AggregatesCorrectly()
    {
        // Arrange - simulate coverage across zoom levels 12-17 (typical tile cache range)
        var zoomCoverage = new List<ZoomCoverageResult>
        {
            new() { Zoom = 12, TotalTiles = 49, CachedTiles = 49, LiveTiles = 25, TripTiles = 24, CoveragePercent = 1.0 },
            new() { Zoom = 13, TotalTiles = 49, CachedTiles = 47, LiveTiles = 30, TripTiles = 17, CoveragePercent = 0.96 },
            new() { Zoom = 14, TotalTiles = 49, CachedTiles = 45, LiveTiles = 35, TripTiles = 10, CoveragePercent = 0.92 },
            new() { Zoom = 15, TotalTiles = 49, CachedTiles = 40, LiveTiles = 30, TripTiles = 10, CoveragePercent = 0.82 },
            new() { Zoom = 16, TotalTiles = 49, CachedTiles = 35, LiveTiles = 25, TripTiles = 10, CoveragePercent = 0.71 },
            new() { Zoom = 17, TotalTiles = 49, CachedTiles = 30, LiveTiles = 20, TripTiles = 10, CoveragePercent = 0.61 }
        };

        // Calculate aggregates
        var totalTiles = zoomCoverage.Sum(z => z.TotalTiles);
        var totalCached = zoomCoverage.Sum(z => z.CachedTiles);
        var totalLive = zoomCoverage.Sum(z => z.LiveTiles);
        var totalTrip = zoomCoverage.Sum(z => z.TripTiles);
        var overallPercent = (double)totalCached / totalTiles;

        // Act
        var result = new DetailedCacheStatusResult
        {
            TotalTiles = totalTiles,
            CachedTiles = totalCached,
            LiveCachedTiles = totalLive,
            TripCachedTiles = totalTrip,
            CoveragePercent = overallPercent,
            ZoomCoverage = zoomCoverage
        };

        // Assert
        result.TotalTiles.Should().Be(294); // 49 * 6
        result.CachedTiles.Should().Be(246); // 49+47+45+40+35+30
        result.LiveCachedTiles.Should().Be(165); // 25+30+35+30+25+20
        result.TripCachedTiles.Should().Be(81);  // 24+17+10+10+10+10
        result.CoveragePercent.Should().BeApproximately(0.837, 0.01); // 246/294
        result.ZoomCoverage.Should().HaveCount(6);
    }

    [Fact]
    public void DetailedCacheStatusResult_NoCoverage_AllZerosExceptTotal()
    {
        // Arrange - offline with no cached tiles
        var zoomCoverage = new List<ZoomCoverageResult>
        {
            new() { Zoom = 14, TotalTiles = 49, CachedTiles = 0, LiveTiles = 0, TripTiles = 0, CoveragePercent = 0.0 },
            new() { Zoom = 15, TotalTiles = 49, CachedTiles = 0, LiveTiles = 0, TripTiles = 0, CoveragePercent = 0.0 },
            new() { Zoom = 16, TotalTiles = 49, CachedTiles = 0, LiveTiles = 0, TripTiles = 0, CoveragePercent = 0.0 }
        };

        // Act
        var result = new DetailedCacheStatusResult
        {
            Status = CacheCoverageStatus.None,
            TotalTiles = 147,
            CachedTiles = 0,
            LiveCachedTiles = 0,
            TripCachedTiles = 0,
            CoveragePercent = 0.0,
            ZoomCoverage = zoomCoverage,
            HasNetwork = false
        };

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.None);
        result.TotalTiles.Should().Be(147);
        result.CachedTiles.Should().Be(0);
        result.CoveragePercent.Should().Be(0.0);
        result.HasNetwork.Should().BeFalse();
    }

    [Fact]
    public void DetailedCacheStatusResult_FullCoverage_AllHundredPercent()
    {
        // Arrange - fully cached area (downloaded trip)
        var zoomCoverage = new List<ZoomCoverageResult>
        {
            new() { Zoom = 12, TotalTiles = 49, CachedTiles = 49, LiveTiles = 0, TripTiles = 49, CoveragePercent = 1.0 },
            new() { Zoom = 14, TotalTiles = 49, CachedTiles = 49, LiveTiles = 0, TripTiles = 49, CoveragePercent = 1.0 },
            new() { Zoom = 17, TotalTiles = 49, CachedTiles = 49, LiveTiles = 0, TripTiles = 49, CoveragePercent = 1.0 }
        };

        // Act
        var result = new DetailedCacheStatusResult
        {
            Status = CacheCoverageStatus.Excellent,
            TotalTiles = 147,
            CachedTiles = 147,
            LiveCachedTiles = 0,
            TripCachedTiles = 147,
            CoveragePercent = 1.0,
            ZoomCoverage = zoomCoverage,
            ActiveTripName = "Downloaded Trip",
            DownloadedTripCount = 1
        };

        // Assert
        result.Status.Should().Be(CacheCoverageStatus.Excellent);
        result.CoveragePercent.Should().Be(1.0);
        result.TripCachedTiles.Should().Be(147);
        result.LiveCachedTiles.Should().Be(0);
        result.ActiveTripName.Should().NotBeNull();
    }

    #endregion
}
