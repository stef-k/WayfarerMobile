using System.Security.Cryptography;
using System.Text;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services.Security;

/// <summary>
/// App lock/PIN security service implementation.
/// Manages 4-state security model:
/// S0: No PIN configured
/// S1: PIN configured, protection disabled
/// S2: PIN configured, protection enabled, session locked
/// S3: PIN configured, protection enabled, session unlocked
/// </summary>
public sealed class AppLockService : IAppLockService
{
    #region Constants

    private const string PinHashKey = "AppPinHash";
    private const string PinSaltKey = "AppPinSalt";
    private const string ProtectionEnabledKey = "ProtectionEnabled";
    private const string LegacyPinCodeKey = "AppPinCode";
    private const string LegacyPinLockEnabledKey = "PinLockEnabled";
    private const int PinLength = 4;
    private const int SaltLength = 32;

    #endregion

    #region State Properties

    /// <inheritdoc/>
    public bool IsPinConfigured { get; private set; }

    /// <inheritdoc/>
    public bool IsProtectionEnabled { get; private set; }

    /// <inheritdoc/>
    public bool IsSessionUnlocked { get; private set; }

    /// <inheritdoc/>
    public bool IsPromptAwaiting { get; private set; }

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        // Migrate legacy storage if needed
        await MigrateLegacyStorageAsync();

        // Load persisted state
        IsProtectionEnabled = Preferences.Get(ProtectionEnabledKey, false);

        // Check for legacy preference key migration
        if (!IsProtectionEnabled)
        {
            bool legacyEnabled = Preferences.Get(LegacyPinLockEnabledKey, false);
            if (legacyEnabled)
            {
                IsProtectionEnabled = true;
                SaveProtectionEnabled();
                Preferences.Remove(LegacyPinLockEnabledKey);
            }
        }

        IsPinConfigured = await HasPinHashAsync();

        // Reset volatile state
        IsSessionUnlocked = false;
        IsPromptAwaiting = false;

        // Invariant: if protection enabled but no PIN, force disable
        if (IsProtectionEnabled && !IsPinConfigured)
        {
            IsProtectionEnabled = false;
            SaveProtectionEnabled();
        }

        System.Diagnostics.Debug.WriteLine($"[AppLockService] Initialized: PinConfigured={IsPinConfigured}, ProtectionEnabled={IsProtectionEnabled}");
    }

    /// <inheritdoc/>
    public void OnAppToBackground()
    {
        // S3 -> S2: Lock session when app goes to background
        IsSessionUnlocked = false;
        IsPromptAwaiting = false;
        System.Diagnostics.Debug.WriteLine("[AppLockService] Session locked (app to background)");
    }

    /// <inheritdoc/>
    public void OnAppToForeground()
    {
        // Remain in current state (locked if protection enabled)
        System.Diagnostics.Debug.WriteLine($"[AppLockService] App to foreground: ProtectionEnabled={IsProtectionEnabled}, SessionUnlocked={IsSessionUnlocked}");
    }

    #endregion

    #region PIN Management

    /// <inheritdoc/>
    public async Task<bool> SetPinAsync(string pin)
    {
        if (!IsValidPin(pin))
        {
            return false;
        }

        try
        {
            // Generate a new salt for this PIN
            byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
            string saltBase64 = Convert.ToBase64String(salt);

            // Hash the PIN with the salt
            string hash = HashPinWithSalt(pin, salt);

            // Store both salt and hash
            await SecureStorage.Default.SetAsync(PinSaltKey, saltBase64);
            await SecureStorage.Default.SetAsync(PinHashKey, hash);

            IsPinConfigured = true;
            System.Diagnostics.Debug.WriteLine("[AppLockService] PIN set successfully with salted hash");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLockService] Error setting PIN: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyPinAsync(string pin)
    {
        if (!IsValidPin(pin))
        {
            return false;
        }

        try
        {
            string? storedHash = await SecureStorage.Default.GetAsync(PinHashKey);
            if (string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            // Get the salt - if no salt exists, this is a legacy hash
            string? saltBase64 = await SecureStorage.Default.GetAsync(PinSaltKey);
            if (string.IsNullOrEmpty(saltBase64))
            {
                // Legacy verification without salt
                string legacyHash = HashPinLegacy(pin);
                return storedHash == legacyHash;
            }

            // Salted verification
            byte[] salt = Convert.FromBase64String(saltBase64);
            string inputHash = HashPinWithSalt(pin, salt);
            return storedHash == inputHash;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLockService] Error verifying PIN: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task RemovePinAsync()
    {
        try
        {
            SecureStorage.Default.Remove(PinHashKey);
            SecureStorage.Default.Remove(PinSaltKey);
            IsPinConfigured = false;
            IsProtectionEnabled = false;
            SaveProtectionEnabled();
            IsSessionUnlocked = false;
            System.Diagnostics.Debug.WriteLine("[AppLockService] PIN and salt removed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLockService] Error removing PIN: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<bool> CheckPinConfiguredAsync()
    {
        IsPinConfigured = await HasPinHashAsync();
        return IsPinConfigured;
    }

    #endregion

    #region Protection Toggle

    /// <inheritdoc/>
    public Task<bool> TryEnableProtectionAsync()
    {
        if (!IsPinConfigured)
        {
            // S0: No PIN - caller should prompt to set PIN first
            return Task.FromResult(false);
        }

        // S1 -> S2: Enable protection and lock
        // Keep session unlocked since user is already authenticated by being on Settings
        IsProtectionEnabled = true;
        SaveProtectionEnabled();
        IsSessionUnlocked = true;

        System.Diagnostics.Debug.WriteLine("[AppLockService] Protection enabled");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public async Task<bool> TryDisableProtectionAsync(string pin)
    {
        if (!IsProtectionEnabled)
        {
            return true; // Already disabled
        }

        // Verify PIN before disabling
        bool verified = await VerifyPinAsync(pin);
        if (!verified)
        {
            return false;
        }

        // S2/S3 -> S1: Disable protection
        IsProtectionEnabled = false;
        SaveProtectionEnabled();
        IsSessionUnlocked = false;

        System.Diagnostics.Debug.WriteLine("[AppLockService] Protection disabled");
        return true;
    }

    #endregion

    #region Session Management

    /// <inheritdoc/>
    public async Task<bool> TryUnlockSessionAsync(string pin)
    {
        bool verified = await VerifyPinAsync(pin);
        if (verified)
        {
            // S2 -> S3: Unlock session
            IsSessionUnlocked = true;
            System.Diagnostics.Debug.WriteLine("[AppLockService] Session unlocked");
        }

        return verified;
    }

    /// <inheritdoc/>
    public bool IsAccessAllowed()
    {
        // Allow if protection disabled or session unlocked
        return !IsProtectionEnabled || IsSessionUnlocked;
    }

    /// <inheritdoc/>
    public void SetPromptAwaiting(bool awaiting)
    {
        IsPromptAwaiting = awaiting;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Migrates legacy PIN storage (unhashed) to new salted hash format.
    /// </summary>
    private async Task MigrateLegacyStorageAsync()
    {
        try
        {
            string? oldPin = await SecureStorage.Default.GetAsync(LegacyPinCodeKey);
            if (!string.IsNullOrEmpty(oldPin))
            {
                // Check if already migrated
                string? newHash = await SecureStorage.Default.GetAsync(PinHashKey);
                if (string.IsNullOrEmpty(newHash))
                {
                    // Migrate: generate salt and hash the old PIN with salt
                    byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
                    string saltBase64 = Convert.ToBase64String(salt);
                    string hash = HashPinWithSalt(oldPin, salt);

                    await SecureStorage.Default.SetAsync(PinSaltKey, saltBase64);
                    await SecureStorage.Default.SetAsync(PinHashKey, hash);
                    System.Diagnostics.Debug.WriteLine("[AppLockService] Migrated legacy PIN storage with salted hash");
                }

                // Remove old storage
                SecureStorage.Default.Remove(LegacyPinCodeKey);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppLockService] Legacy migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a PIN hash exists in secure storage.
    /// </summary>
    private async Task<bool> HasPinHashAsync()
    {
        try
        {
            string? hash = await SecureStorage.Default.GetAsync(PinHashKey);
            return !string.IsNullOrEmpty(hash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Saves the protection enabled state to preferences.
    /// </summary>
    private void SaveProtectionEnabled()
    {
        Preferences.Set(ProtectionEnabledKey, IsProtectionEnabled);
    }

    /// <summary>
    /// Validates that a PIN is exactly 4 digits.
    /// </summary>
    private static bool IsValidPin(string? pin)
    {
        return !string.IsNullOrWhiteSpace(pin) &&
               pin.Length == PinLength &&
               pin.All(char.IsDigit);
    }

    /// <summary>
    /// Hashes a PIN using SHA256 with a salt for improved security.
    /// </summary>
    /// <param name="pin">The PIN to hash.</param>
    /// <param name="salt">The random salt bytes.</param>
    /// <returns>Base64-encoded hash of salt+PIN.</returns>
    private static string HashPinWithSalt(string pin, byte[] salt)
    {
        byte[] pinBytes = Encoding.UTF8.GetBytes(pin);

        // Combine salt and PIN bytes
        byte[] saltedPin = new byte[salt.Length + pinBytes.Length];
        Buffer.BlockCopy(salt, 0, saltedPin, 0, salt.Length);
        Buffer.BlockCopy(pinBytes, 0, saltedPin, salt.Length, pinBytes.Length);

        byte[] hash = SHA256.HashData(saltedPin);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Hashes a PIN using SHA256 without salt (legacy method for backward compatibility).
    /// </summary>
    /// <param name="pin">The PIN to hash.</param>
    /// <returns>Base64-encoded hash of PIN.</returns>
    private static string HashPinLegacy(string pin)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(pin);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    #endregion
}
