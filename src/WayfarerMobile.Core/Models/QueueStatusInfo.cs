namespace WayfarerMobile.Core.Models;

/// <summary>
/// Queue status information for Settings UI display.
/// </summary>
public class QueueStatusInfo
{
    /// <summary>
    /// Gets the total count of all queued locations.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Gets the count of pending locations (excludes retrying).
    /// </summary>
    public int PendingCount { get; init; }

    /// <summary>
    /// Gets the count of locations currently retrying (pending with SyncAttempts > 0).
    /// </summary>
    public int RetryingCount { get; init; }

    /// <summary>
    /// Gets the count of locations currently syncing.
    /// </summary>
    public int SyncingCount { get; init; }

    /// <summary>
    /// Gets the count of successfully synced locations.
    /// </summary>
    public int SyncedCount { get; init; }

    /// <summary>
    /// Gets the count of rejected locations.
    /// </summary>
    public int RejectedCount { get; init; }

    /// <summary>
    /// Gets the configured queue limit.
    /// </summary>
    public int QueueLimit { get; init; }

    /// <summary>
    /// Gets the timestamp of the oldest pending location.
    /// </summary>
    public DateTime? OldestPendingTimestamp { get; init; }

    /// <summary>
    /// Gets the timestamp of the newest pending location.
    /// </summary>
    public DateTime? NewestPendingTimestamp { get; init; }

    /// <summary>
    /// Gets the timestamp of the last synced location.
    /// </summary>
    public DateTime? LastSyncedTimestamp { get; init; }

    /// <summary>
    /// Gets the usage percentage (TotalCount / QueueLimit * 100).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Gets whether the queue is over the configured limit.
    /// Over limit when count exceeds limit (not at exactly 100%).
    /// </summary>
    public bool IsOverLimit => TotalCount > QueueLimit;

    /// <summary>
    /// Gets the current coverage span (newest - oldest pending).
    /// </summary>
    public TimeSpan? CurrentCoverageSpan =>
        OldestPendingTimestamp.HasValue && NewestPendingTimestamp.HasValue
            ? NewestPendingTimestamp.Value - OldestPendingTimestamp.Value
            : null;

    /// <summary>
    /// Calculates estimated remaining headroom based on time threshold.
    /// </summary>
    /// <param name="timeThresholdMinutes">The location time threshold in minutes.</param>
    /// <returns>Estimated time until queue is full.</returns>
    public TimeSpan GetRemainingHeadroom(int timeThresholdMinutes)
    {
        if (timeThresholdMinutes <= 0 || QueueLimit <= 0)
            return TimeSpan.Zero;

        var slotsRemaining = Math.Max(0, QueueLimit - TotalCount);
        return TimeSpan.FromMinutes(slotsRemaining * timeThresholdMinutes);
    }

    /// <summary>
    /// Gets the health status based on usage percentage.
    /// </summary>
    public string HealthStatus
    {
        get
        {
            if (QueueLimit <= 0) return "Unknown";
            if (TotalCount > QueueLimit) return "Over Limit";

            var percent = UsagePercent;
            return percent switch
            {
                >= 95 => "Critical",
                >= 80 => "Warning",
                _ => "Healthy"
            };
        }
    }
}
