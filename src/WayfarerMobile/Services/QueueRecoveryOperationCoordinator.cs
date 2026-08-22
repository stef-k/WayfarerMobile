namespace WayfarerMobile.Services;

/// <summary>Serializes in-process recovery preparation, export, and resume operations.</summary>
public sealed class QueueRecoveryOperationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for exclusive ownership of the recovery workflow.</summary>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
