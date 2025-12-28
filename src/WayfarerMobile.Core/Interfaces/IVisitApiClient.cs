using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Minimal API client interface for visit-related operations.
/// Used by Core services that need to poll for visits without depending on full IApiClient.
/// </summary>
public interface IVisitApiClient
{
    /// <summary>
    /// Gets recent visits from the server (for background polling when SSE is unavailable).
    /// </summary>
    /// <param name="sinceSeconds">Number of seconds to look back for visits (default 30).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent visit events, or empty list on failure.</returns>
    Task<List<SseVisitStartedEvent>> GetRecentVisitsAsync(
        int sinceSeconds = 30,
        CancellationToken cancellationToken = default);
}
