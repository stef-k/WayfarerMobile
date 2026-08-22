using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Establishes the queue recovery boundary before producing and sharing an export.
/// </summary>
public sealed class RecoveryExportCoordinator
{
    private readonly QueueDrainService _queueDrainService;
    private readonly IQueueExportService _exportService;
    private readonly ILocationQueueRepository? _repository;
    private readonly SemaphoreSlim _exportGate = new(1, 1);

    /// <summary>Creates a recovery export coordinator.</summary>
    public RecoveryExportCoordinator(QueueDrainService queueDrainService, IQueueExportService exportService)
        : this(queueDrainService, exportService, null)
    {
    }

    /// <summary>Creates a recovery export coordinator with empty-queue detection.</summary>
    public RecoveryExportCoordinator(
        QueueDrainService queueDrainService,
        IQueueExportService exportService,
        ILocationQueueRepository? repository)
    {
        _queueDrainService = queueDrainService;
        _exportService = exportService;
        _repository = repository;
    }

    /// <summary>Suspends delivery, waits for quiescence, then creates and shares the canonical export.</summary>
    /// <returns><see langword="true"/> when an export was shared; otherwise <see langword="false"/>.</returns>
    public async Task<bool> ExportAndShareAsync(string format, CancellationToken cancellationToken = default)
    {
        await _exportGate.WaitAsync(cancellationToken);
        try
        {
            await _queueDrainService.SuspendAndWaitForQuiescenceAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_repository != null && (await _repository.GetAllQueuedLocationsForExportAsync()).Count == 0)
                return false;

            await _exportService.ShareExportAsync(format);
            return true;
        }
        finally
        {
            _exportGate.Release();
        }
    }
}
