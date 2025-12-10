namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for handling application lifecycle events.
/// Manages state persistence and restoration across app suspend/resume cycles.
/// </summary>
public interface IAppLifecycleService
{
    /// <summary>
    /// Event raised when the app is about to go to the background.
    /// </summary>
    event EventHandler? AppSuspending;

    /// <summary>
    /// Event raised when the app is resuming from the background.
    /// </summary>
    event EventHandler? AppResuming;

    /// <summary>
    /// Called when the app is going to the background.
    /// Saves state and prepares for suspension.
    /// </summary>
    Task OnSuspendingAsync();

    /// <summary>
    /// Called when the app is resuming from the background.
    /// Restores state and resumes operations.
    /// </summary>
    Task OnResumingAsync();

    /// <summary>
    /// Gets the last saved navigation state, if any.
    /// </summary>
    NavigationStateSnapshot? GetSavedNavigationState();

    /// <summary>
    /// Clears any saved navigation state.
    /// </summary>
    void ClearNavigationState();
}

/// <summary>
/// Snapshot of navigation state for persistence across app lifecycle.
/// </summary>
public class NavigationStateSnapshot
{
    /// <summary>
    /// Gets or sets whether navigation was active.
    /// </summary>
    public bool WasNavigating { get; set; }

    /// <summary>
    /// Gets or sets the destination name.
    /// </summary>
    public string? DestinationName { get; set; }

    /// <summary>
    /// Gets or sets the destination place ID.
    /// </summary>
    public string? DestinationPlaceId { get; set; }

    /// <summary>
    /// Gets or sets the trip ID being navigated.
    /// </summary>
    public Guid? TripId { get; set; }

    /// <summary>
    /// Gets or sets when the state was saved.
    /// </summary>
    public DateTime SavedAt { get; set; }
}
