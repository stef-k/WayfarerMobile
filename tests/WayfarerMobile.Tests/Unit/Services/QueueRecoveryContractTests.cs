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

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
