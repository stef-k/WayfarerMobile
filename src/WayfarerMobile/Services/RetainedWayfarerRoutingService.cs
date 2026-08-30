using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

public sealed class RetainedWayfarerRoutingService
{
    private readonly RetainedWayfarerRouteRepository repository;
    private readonly ILogger<RetainedWayfarerRoutingService> logger;

    public RetainedWayfarerRoutingService(RetainedWayfarerRouteRepository repository,
        ILogger<RetainedWayfarerRoutingService> logger)
    {
        this.repository = repository;
        this.logger = logger;
    }

    public async Task<NavigationRoute?> TrySelectOfflineAsync(HostedRouteRequestContext context,
        Guid accountPartition, DateTimeOffset nowUtc, Func<bool> isCurrent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var selected = await repository.SelectAsync(context, accountPartition, nowUtc,
                isCurrent, cancellationToken);
            return selected?.Route;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Retained Wayfarer route selection failed locally");
            return null;
        }
    }

    public async Task<RetainedRouteSaveResult> SaveAsync(HostedRouteCandidate candidate, Guid accountPartition,
        DateTimeOffset receiptTimeUtc, Func<bool> isCurrent, CancellationToken cancellationToken = default)
    {
        try
        {
            return await repository.SaveAsync(candidate, accountPartition, receiptTimeUtc,
                isCurrent, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("Retained Wayfarer route save failed locally: {FailureType}",
                exception.GetType().Name);
            return RetainedRouteSaveResult.Failed;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => repository.ClearAsync(cancellationToken);
}
