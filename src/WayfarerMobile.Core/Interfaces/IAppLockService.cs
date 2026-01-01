namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for app lock/PIN security service.
/// Manages 4-state security model:
/// S0: No PIN configured
/// S1: PIN configured, protection disabled
/// S2: PIN configured, protection enabled, session locked
/// S3: PIN configured, protection enabled, session unlocked
/// </summary>
public interface IAppLockService
{
    #region State Properties

    /// <summary>
    /// Gets whether a PIN is currently configured.
    /// </summary>
    bool IsPinConfigured { get; }

    /// <summary>
    /// Gets whether protection is currently enabled.
    /// </summary>
    bool IsProtectionEnabled { get; }

    /// <summary>
    /// Gets whether the current session is unlocked.
    /// </summary>
    bool IsSessionUnlocked { get; }

    /// <summary>
    /// Gets whether a PIN prompt is currently being shown (prevents re-entry).
    /// </summary>
    bool IsPromptAwaiting { get; }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initializes the service on app cold start.
    /// Loads persisted state and resets session.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Called when app goes to background. Locks session.
    /// S3 -> S2 transition.
    /// </summary>
    void OnAppToBackground();

    /// <summary>
    /// Called when app returns to foreground.
    /// </summary>
    void OnAppToForeground();

    #endregion

    #region PIN Management

    /// <summary>
    /// Sets or changes the PIN code.
    /// </summary>
    /// <param name="pin">The 4-digit PIN to set.</param>
    /// <returns>True if PIN was set successfully.</returns>
    Task<bool> SetPinAsync(string pin);

    /// <summary>
    /// Verifies if the provided PIN matches the stored PIN.
    /// </summary>
    /// <param name="pin">The PIN to verify.</param>
    /// <returns>True if PIN matches.</returns>
    Task<bool> VerifyPinAsync(string pin);

    /// <summary>
    /// Removes the configured PIN and disables protection.
    /// </summary>
    Task RemovePinAsync();

    /// <summary>
    /// Checks if a PIN is configured (async version that re-reads from storage).
    /// </summary>
    Task<bool> CheckPinConfiguredAsync();

    #endregion

    #region Protection Toggle

    /// <summary>
    /// Attempts to enable protection.
    /// Returns false if no PIN is configured (caller should prompt to set PIN first).
    /// S1 -> S2 transition.
    /// </summary>
    /// <returns>True if protection was enabled.</returns>
    Task<bool> TryEnableProtectionAsync();

    /// <summary>
    /// Attempts to disable protection.
    /// Requires PIN verification.
    /// S2/S3 -> S1 transition.
    /// </summary>
    /// <param name="pin">The PIN to verify before disabling.</param>
    /// <returns>True if protection was disabled.</returns>
    Task<bool> TryDisableProtectionAsync(string pin);

    #endregion

    #region Session Management

    /// <summary>
    /// Attempts to unlock the session with the provided PIN.
    /// S2 -> S3 transition.
    /// </summary>
    /// <param name="pin">The PIN to verify.</param>
    /// <returns>True if session was unlocked.</returns>
    Task<bool> TryUnlockSessionAsync(string pin);

    /// <summary>
    /// Checks if access to a protected resource is allowed.
    /// Returns true if protection is disabled or session is unlocked.
    /// </summary>
    /// <returns>True if access is allowed.</returns>
    bool IsAccessAllowed();

    /// <summary>
    /// Sets the prompt awaiting flag (to prevent re-entry during PIN prompts).
    /// </summary>
    /// <param name="awaiting">True if a prompt is being shown.</param>
    void SetPromptAwaiting(bool awaiting);

    #endregion
}
