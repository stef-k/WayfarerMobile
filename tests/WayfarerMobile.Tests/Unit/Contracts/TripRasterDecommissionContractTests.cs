namespace WayfarerMobile.Tests.Unit.Contracts;

public sealed class TripRasterDecommissionContractTests
{
    [Fact]
    public void TripDownloadAndSynchronization_DoNotOwnRasterAcquisition()
    {
        var root = FindRepositoryRoot();
        string[] paths =
        [
            "src/WayfarerMobile/Services/TripDownloadService.cs",
            "src/WayfarerMobile/Services/TripSyncCoordinator.cs",
            "src/WayfarerMobile/Interfaces/ITripDownloadService.cs"
        ];
        var sources = string.Join('\n', paths.Select(path => File.ReadAllText(Path.Combine(root, path))));

        sources.Should().NotContain("ITileDownloadService");
        sources.Should().NotContain("ITileDownloadOrchestrator");
        sources.Should().NotContain("ITripTileRepository");
        sources.Should().NotContain("DownloadTilesAsync");
        sources.Should().NotContain("DeleteTripTilesAsync");
        sources.Should().Contain("DownloadTripAsync");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
