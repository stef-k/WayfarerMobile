namespace WayfarerMobile.Tests.Unit.Contracts;

public sealed class RasterDecommissionContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ObsoleteRasterOwnersAndSettings_AreAbsentFromProduction()
    {
        string[] removedPaths =
        [
            "src/WayfarerMobile/Services/TileDownloadService.cs",
            "src/WayfarerMobile/Services/TileDownloadOrchestrator.cs",
            "src/WayfarerMobile/Data/Repositories/TripTileRepository.cs",
            "src/WayfarerMobile/Data/Repositories/DownloadStateRepository.cs",
            "src/WayfarerMobile/Data/Entities/TripTile.cs",
            "src/WayfarerMobile/Data/Entities/TripDownloadState.cs"
        ];
        foreach (var path in removedPaths)
            File.Exists(Path.Combine(RepositoryRoot, path)).Should().BeFalse(path);

        var production = ReadProductionSources();
        string[] removedTokens =
        [
            "MapOfflineCacheEnabled", "LiveCachePrefetchRadius", "PrefetchDistanceThresholdMeters",
            "MaxTripCacheSizeMB", "MaxConcurrentTileDownloads", "MinTileRequestDelayMs",
            "PrefetchAroundLocationAsync", "DownloadTilesAsync"
        ];
        foreach (var token in removedTokens)
            production.Should().NotContain(token);
    }

    [Fact]
    public void SettingsPresentation_KeepsLiveCacheControls_AndAttributionHasCopyrightUrl()
    {
        var settings = File.ReadAllText(Path.Combine(RepositoryRoot, "src/WayfarerMobile/Views/SettingsPage.xaml"));
        settings.Should().Contain("MaxLiveCacheSizeMB");
        settings.Should().Contain("ClearLiveCacheCommand");
        settings.Should().NotContain("LiveCachePrefetchRadius");
        settings.Should().NotContain("MaxTripCacheSizeMB");

        var tileSource = File.ReadAllText(Path.Combine(RepositoryRoot, "src/WayfarerMobile/Services/TileCache/WayfarerTileSource.cs"));
        tileSource.Should().Contain("© OpenStreetMap contributors");
        tileSource.Should().Contain("https://www.openstreetmap.org/copyright");
    }

    private static string ReadProductionSources() => string.Join('\n',
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.xaml", SearchOption.AllDirectories))
            .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
