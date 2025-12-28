using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for handling visit notifications via SSE.
/// Subscribes to visit_started events and shows notifications/announcements
/// when the user arrives at a trip place.
/// </summary>
public interface IVisitNotificationService : IDisposable
{
    /// <summary>
    /// Gets whether the service is currently subscribed to visit events.
    /// </summary>
    bool IsSubscribed { get; }

    /// <summary>
    /// Starts subscribing to visit SSE events.
    /// Only subscribes if the feature is enabled in settings.
    /// </summary>
    /// <returns>Task that completes when subscription starts.</returns>
    Task StartAsync();

    /// <summary>
    /// Stops the visit SSE subscription.
    /// </summary>
    void Stop();

    /// <summary>
    /// Updates the current navigation state for conflict detection.
    /// Call this when navigation starts or stops.
    /// </summary>
    /// <param name="isNavigating">Whether navigation is currently active.</param>
    /// <param name="destinationPlaceId">The ID of the destination place (if navigating).</param>
    void UpdateNavigationState(bool isNavigating, Guid? destinationPlaceId);

    /// <summary>
    /// Raised when a visit notification is displayed to the user.
    /// </summary>
    event EventHandler<VisitNotificationEventArgs>? NotificationDisplayed;
}

/// <summary>
/// Event arguments for visit notifications.
/// </summary>
public class VisitNotificationEventArgs : EventArgs
{
    /// <summary>
    /// The visit event that triggered the notification.
    /// </summary>
    public SseVisitStartedEvent Visit { get; }

    /// <summary>
    /// The notification mode that was used.
    /// </summary>
    public VisitNotificationMode Mode { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public VisitNotificationEventArgs(SseVisitStartedEvent visit, VisitNotificationMode mode)
    {
        Visit = visit;
        Mode = mode;
    }
}

/// <summary>
/// The mode in which a visit notification was displayed.
/// </summary>
public enum VisitNotificationMode
{
    /// <summary>
    /// Full notification with sound and/or vibration.
    /// </summary>
    Full,

    /// <summary>
    /// Silent notification (during navigation to a different place).
    /// </summary>
    Silent,

    /// <summary>
    /// Notification was suppressed (during navigation to the same place).
    /// </summary>
    Suppressed
}
