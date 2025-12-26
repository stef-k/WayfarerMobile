using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the PIN Security section in Settings.
/// Manages the 4-state PIN security machine (S0→S1→S2→S3).
/// </summary>
public partial class PinSecurityViewModel : ObservableObject
{
    #region Fields

    private readonly IAppLockService _appLockService;
    private bool _isLoadingSettings;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether PIN lock is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isPinLockEnabled;

    /// <summary>
    /// Gets or sets whether a PIN is configured.
    /// </summary>
    [ObservableProperty]
    private bool _isPinConfigured;

    /// <summary>
    /// Gets or sets the text for the PIN setup button.
    /// </summary>
    [ObservableProperty]
    private string _setPinButtonText = "Set PIN Code";

    /// <summary>
    /// Gets or sets whether the view is busy.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of PinSecurityViewModel.
    /// </summary>
    /// <param name="appLockService">The app lock service.</param>
    public PinSecurityViewModel(IAppLockService appLockService)
    {
        _appLockService = appLockService ?? throw new ArgumentNullException(nameof(appLockService));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads PIN lock settings from the service.
    /// Should be called when Settings page loads.
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        IsBusy = true;

        try
        {
            IsPinConfigured = await _appLockService.CheckPinConfiguredAsync();
            IsPinLockEnabled = _appLockService.IsProtectionEnabled;
            UpdateButtonText();

            System.Diagnostics.Debug.WriteLine($"[PinSecurityViewModel] Settings loaded: PinConfigured={IsPinConfigured}, LockEnabled={IsPinLockEnabled}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinSecurityViewModel] Error loading settings: {ex.Message}");
        }
        finally
        {
            _isLoadingSettings = false;
            IsBusy = false;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Command to set or change the PIN code.
    /// </summary>
    [RelayCommand]
    private async Task SetOrChangePinAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var currentPage = GetCurrentPage();
            if (currentPage == null) return;

            // If PIN exists, verify current PIN first
            if (IsPinConfigured)
            {
                string? currentPin = await currentPage.DisplayPromptAsync(
                    "Verify Current PIN",
                    "Enter your current 4-digit PIN",
                    "OK",
                    "Cancel",
                    maxLength: 4,
                    keyboard: Keyboard.Numeric);

                if (string.IsNullOrEmpty(currentPin))
                {
                    return; // Canceled
                }

                bool verified = await _appLockService.VerifyPinAsync(currentPin);
                if (!verified)
                {
                    await currentPage.DisplayAlertAsync("Error", "Incorrect PIN code", "OK");
                    return;
                }
            }

            // Get new PIN
            string? newPin = await currentPage.DisplayPromptAsync(
                "Set PIN Code",
                "Enter a 4-digit PIN code",
                "OK",
                "Cancel",
                maxLength: 4,
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(newPin))
            {
                return; // Canceled
            }

            if (!IsValidPin(newPin))
            {
                await currentPage.DisplayAlertAsync("Invalid PIN", "PIN must be exactly 4 digits (0-9).", "OK");
                return;
            }

            // Confirm new PIN
            string? confirmPin = await currentPage.DisplayPromptAsync(
                "Confirm PIN Code",
                "Enter the same 4-digit PIN again",
                "OK",
                "Cancel",
                maxLength: 4,
                keyboard: Keyboard.Numeric);

            if (newPin != confirmPin)
            {
                await currentPage.DisplayAlertAsync("PIN Mismatch", "The PIN codes don't match. Please try again.", "OK");
                return;
            }

            // Save PIN (S0 → S1 transition)
            bool wasConfigured = IsPinConfigured;
            bool success = await _appLockService.SetPinAsync(newPin);

            if (!success)
            {
                await currentPage.DisplayAlertAsync("Error", "Failed to set PIN code.", "OK");
                return;
            }

            IsPinConfigured = true;
            UpdateButtonText();

            string message = wasConfigured
                ? "PIN code changed successfully."
                : "PIN code set successfully. You can now enable PIN lock.";
            await currentPage.DisplayAlertAsync("Success", message, "OK");

            // If first PIN and lock not enabled, ask to enable
            if (!wasConfigured && !_appLockService.IsProtectionEnabled)
            {
                bool enableNow = await currentPage.DisplayAlertAsync(
                    "Enable PIN Lock?",
                    "Would you like to enable PIN lock now?",
                    "Enable Now",
                    "Later");

                if (enableNow)
                {
                    await EnableProtectionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinSecurityViewModel] Error setting PIN: {ex.Message}");
            var page = GetCurrentPage();
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", "Failed to set PIN code.", "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Command to remove the PIN code.
    /// </summary>
    [RelayCommand]
    private async Task RemovePinAsync()
    {
        if (IsBusy || !IsPinConfigured) return;
        IsBusy = true;

        try
        {
            var currentPage = GetCurrentPage();
            if (currentPage == null) return;

            // Verify PIN before removal
            string? pin = await currentPage.DisplayPromptAsync(
                "Verify PIN",
                "Enter your current 4-digit PIN to remove it",
                "OK",
                "Cancel",
                maxLength: 4,
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(pin))
            {
                return; // Canceled
            }

            bool verified = await _appLockService.VerifyPinAsync(pin);
            if (!verified)
            {
                await currentPage.DisplayAlertAsync("Error", "Incorrect PIN code", "OK");
                return;
            }

            // Confirm removal
            bool confirm = await currentPage.DisplayAlertAsync(
                "Remove PIN?",
                "This will disable PIN lock and remove your PIN code. Continue?",
                "Remove",
                "Cancel");

            if (!confirm)
            {
                return;
            }

            await _appLockService.RemovePinAsync();
            IsPinConfigured = false;
            IsPinLockEnabled = false;
            UpdateButtonText();

            await currentPage.DisplayAlertAsync("Success", "PIN code removed.", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinSecurityViewModel] Error removing PIN: {ex.Message}");
            var page = GetCurrentPage();
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", "Failed to remove PIN code.", "OK");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Property Changed Handlers

    /// <summary>
    /// Handles changes to the IsPinLockEnabled property.
    /// </summary>
    partial void OnIsPinLockEnabledChanged(bool value)
    {
        // Skip during settings load to prevent circular trigger
        if (_isLoadingSettings) return;

        _ = HandlePinLockToggleAsync(value);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Gets the current page for displaying alerts.
    /// </summary>
    private static Page? GetCurrentPage()
    {
        return Application.Current?.Windows.FirstOrDefault()?.Page;
    }

    /// <summary>
    /// Handles PIN lock toggle changes asynchronously.
    /// </summary>
    private async Task HandlePinLockToggleAsync(bool isEnabled)
    {
        IsBusy = true;

        try
        {
            if (isEnabled)
            {
                await EnableProtectionAsync();
            }
            else
            {
                await DisableProtectionAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PinSecurityViewModel] Error toggling PIN lock: {ex.Message}");
            var page = GetCurrentPage();
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", "Failed to update PIN lock setting.", "OK");
            }
            // Revert toggle
            _isLoadingSettings = true;
            IsPinLockEnabled = !isEnabled;
            _isLoadingSettings = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Enables PIN protection.
    /// </summary>
    private async Task EnableProtectionAsync()
    {
        bool success = await _appLockService.TryEnableProtectionAsync();

        if (!success)
        {
            // No PIN configured - revert toggle
            var page = GetCurrentPage();
            if (page != null)
            {
                await page.DisplayAlertAsync(
                    "Set PIN First",
                    "Please set a PIN code before enabling PIN lock.",
                    "OK");
            }
            _isLoadingSettings = true;
            IsPinLockEnabled = false;
            _isLoadingSettings = false;
        }
        else
        {
            // Update toggle state to reflect enabled protection
            _isLoadingSettings = true;
            IsPinLockEnabled = true;
            _isLoadingSettings = false;

            var page = GetCurrentPage();
            if (page != null)
            {
                await page.DisplayAlertAsync(
                    "PIN Lock Enabled",
                    "PIN lock is now active. You'll need to enter your PIN when accessing protected menu items.",
                    "OK");
            }
        }
    }

    /// <summary>
    /// Disables PIN protection with PIN verification.
    /// </summary>
    private async Task DisableProtectionAsync()
    {
        var currentPage = GetCurrentPage();
        if (currentPage == null) return;

        // Prompt for PIN verification
        string? pin = await currentPage.DisplayPromptAsync(
            "Verify PIN to Disable",
            "Enter your 4-digit PIN code",
            "OK",
            "Cancel",
            maxLength: 4,
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrEmpty(pin))
        {
            // Canceled - revert toggle
            _isLoadingSettings = true;
            IsPinLockEnabled = true;
            _isLoadingSettings = false;
            return;
        }

        bool success = await _appLockService.TryDisableProtectionAsync(pin);

        if (!success)
        {
            // Wrong PIN - revert toggle
            await currentPage.DisplayAlertAsync("Error", "Incorrect PIN code", "OK");
            _isLoadingSettings = true;
            IsPinLockEnabled = true;
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Updates the PIN button text based on configuration state.
    /// </summary>
    private void UpdateButtonText()
    {
        SetPinButtonText = IsPinConfigured ? "Change PIN Code" : "Set PIN Code";
    }

    /// <summary>
    /// Validates that a PIN is exactly 4 digits.
    /// </summary>
    private static bool IsValidPin(string? pin)
    {
        return !string.IsNullOrWhiteSpace(pin) &&
               pin.Length == 4 &&
               pin.All(char.IsDigit);
    }

    #endregion
}
