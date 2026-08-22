using System.Text.Json;

namespace WayfarerMobile.Tests.Unit.Services;

public class QueueRecoveryContractTests
{
    [Fact]
    public void CanonicalRecoveryExports_ExposePortableIdempotencyKey()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "WayfarerMobile", "Services", "QueueExportService.cs"));

        Assert.Contains("IdempotencyKey", source, StringComparison.Ordinal);
        Assert.Contains("loc.IdempotencyKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DrainService_OwnsPersistedSuspensionAndQuiescence()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "WayfarerMobile", "Services", "QueueDrainService.cs"));

        Assert.Contains("SuspendAndWaitForQuiescenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("QueueDeliverySuspended", source, StringComparison.Ordinal);
        Assert.Contains("ResumeAndReconcileAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BothRecoveryFormatCommands_DelegateToTheSharedWorkflow()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "WayfarerMobile", "ViewModels", "Settings", "OfflineQueueSettingsViewModel.cs"));

        Assert.Contains("ExportRecoveryAsync(\"csv\", \"CSV\")", source, StringComparison.Ordinal);
        Assert.Contains("ExportRecoveryAsync(\"geojson\", \"GeoJSON\")", source, StringComparison.Ordinal);
        Assert.Contains("_recoveryExportCoordinator.ExportAndShareAsync(format)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryCommands_UseSharedCoordinationAndDisableCompetingUiWork()
    {
        var drainSource = File.ReadAllText(FindRepositoryFile(
            "src", "WayfarerMobile", "Services", "QueueDrainService.cs"));
        var exportSource = File.ReadAllText(FindRepositoryFile(
            "src", "WayfarerMobile", "Services", "RecoveryExportCoordinator.cs"));
        var xaml = File.ReadAllText(FindRepositoryFile(
            "src", "WayfarerMobile", "Views", "SettingsPage.xaml"));

        Assert.Contains("_recoveryOperations.AcquireAsync(cancellationToken)", drainSource, StringComparison.Ordinal);
        Assert.Contains("_recoveryOperations.AcquireAsync()", drainSource, StringComparison.Ordinal);
        Assert.Contains("_recoveryOperations.AcquireAsync(cancellationToken)", exportSource, StringComparison.Ordinal);
        Assert.Equal(4, xaml.Split("OfflineQueue.IsPreparingRecovery", StringSplitOptions.None).Length - 1);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
