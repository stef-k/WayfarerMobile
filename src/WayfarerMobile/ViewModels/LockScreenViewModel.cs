using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the lock screen PIN entry overlay.
/// Handles PIN verification, attempt tracking, and lockout delays.
/// </summary>
public partial class LockScreenViewModel : BaseViewModel
{
    private readonly IAppLockService _appLockService;

    /// <summary>
    /// Maximum failed attempts before lockout delay.
    /// </summary>
    private const int MaxAttempts = 3;

    /// <summary>
    /// Lockout delay in seconds after max failed attempts.
    /// </summary>
    private const int LockoutDelaySeconds = 30;

    /// <summary>
    /// The entered PIN digits (up to 4).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Digit1))]
    [NotifyPropertyChangedFor(nameof(Digit2))]
    [NotifyPropertyChangedFor(nameof(Digit3))]
    [NotifyPropertyChangedFor(nameof(Digit4))]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    private string _enteredPin = string.Empty;

    /// <summary>
    /// Error message to display after failed verification.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Number of remaining attempts before lockout.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAttemptsWarning))]
    private int _attemptsRemaining = MaxAttempts;

    /// <summary>
    /// Whether the user is currently locked out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterPin))]
    private bool _isLockedOut;

    /// <summary>
    /// Countdown seconds remaining during lockout.
    /// </summary>
    [ObservableProperty]
    private int _lockoutSecondsRemaining;

    /// <summary>
    /// Whether verification is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterPin))]
    private bool _isVerifying;

    /// <summary>
    /// Whether to trigger a shake animation for invalid PIN.
    /// </summary>
    [ObservableProperty]
    private bool _shakeError;

    /// <summary>
    /// Event raised when the session is successfully unlocked.
    /// </summary>
    public event EventHandler? SessionUnlocked;

    /// <summary>
    /// Gets whether PIN entry is allowed.
    /// </summary>
    public bool CanEnterPin => !IsLockedOut && !IsVerifying;

    /// <summary>
    /// Gets whether the verify command can execute.
    /// </summary>
    public bool CanVerify => EnteredPin.Length == 4 && CanEnterPin;

    /// <summary>
    /// Gets whether to show the attempts warning.
    /// </summary>
    public bool ShowAttemptsWarning => AttemptsRemaining < MaxAttempts && AttemptsRemaining > 0;

    /// <summary>
    /// Gets the first PIN digit display character.
    /// </summary>
    public string Digit1 => EnteredPin.Length >= 1 ? "●" : "";

    /// <summary>
    /// Gets the second PIN digit display character.
    /// </summary>
    public string Digit2 => EnteredPin.Length >= 2 ? "●" : "";

    /// <summary>
    /// Gets the third PIN digit display character.
    /// </summary>
    public string Digit3 => EnteredPin.Length >= 3 ? "●" : "";

    /// <summary>
    /// Gets the fourth PIN digit display character.
    /// </summary>
    public string Digit4 => EnteredPin.Length >= 4 ? "●" : "";

    /// <summary>
    /// Creates a new instance of LockScreenViewModel.
    /// </summary>
    /// <param name="appLockService">The app lock service.</param>
    public LockScreenViewModel(IAppLockService appLockService)
    {
        _appLockService = appLockService;
        Title = "Enter PIN";
    }

    /// <summary>
    /// Appends a digit to the entered PIN.
    /// </summary>
    /// <param name="digit">The digit to append (0-9).</param>
    [RelayCommand]
    private async Task EnterDigitAsync(string digit)
    {
        if (!CanEnterPin || EnteredPin.Length >= 4)
        {
            return;
        }

        EnteredPin += digit;
        ErrorMessage = string.Empty;
        ShakeError = false;

        // Auto-verify when 4 digits entered
        if (EnteredPin.Length == 4)
        {
            await VerifyPinAsync();
        }
    }

    /// <summary>
    /// Deletes the last entered digit.
    /// </summary>
    [RelayCommand]
    private void DeleteDigit()
    {
        if (!CanEnterPin || EnteredPin.Length == 0)
        {
            return;
        }

        EnteredPin = EnteredPin[..^1];
        ErrorMessage = string.Empty;
        ShakeError = false;
    }

    /// <summary>
    /// Clears all entered digits.
    /// </summary>
    [RelayCommand]
    private void ClearPin()
    {
        if (!CanEnterPin)
        {
            return;
        }

        EnteredPin = string.Empty;
        ErrorMessage = string.Empty;
        ShakeError = false;
    }

    /// <summary>
    /// Verifies the entered PIN.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyPinAsync()
    {
        if (EnteredPin.Length != 4)
        {
            return;
        }

        IsVerifying = true;
        _appLockService.SetPromptAwaiting(true);

        try
        {
            var success = await _appLockService.TryUnlockSessionAsync(EnteredPin);

            if (success)
            {
                // Reset state
                AttemptsRemaining = MaxAttempts;
                ErrorMessage = string.Empty;
                EnteredPin = string.Empty;

                // Notify success
                SessionUnlocked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // Failed attempt
                AttemptsRemaining--;
                EnteredPin = string.Empty;
                ShakeError = true;

                if (AttemptsRemaining <= 0)
                {
                    // Start lockout
                    await StartLockoutAsync();
                }
                else
                {
                    ErrorMessage = $"Incorrect PIN. {AttemptsRemaining} attempt{(AttemptsRemaining == 1 ? "" : "s")} remaining.";
                }
            }
        }
        finally
        {
            IsVerifying = false;
            _appLockService.SetPromptAwaiting(false);
        }
    }

    /// <summary>
    /// Starts the lockout countdown timer.
    /// </summary>
    private async Task StartLockoutAsync()
    {
        IsLockedOut = true;
        LockoutSecondsRemaining = LockoutDelaySeconds;
        ErrorMessage = $"Too many failed attempts. Try again in {LockoutSecondsRemaining} seconds.";

        while (LockoutSecondsRemaining > 0)
        {
            await Task.Delay(1000);
            LockoutSecondsRemaining--;
            ErrorMessage = $"Too many failed attempts. Try again in {LockoutSecondsRemaining} seconds.";
        }

        // Reset after lockout
        IsLockedOut = false;
        AttemptsRemaining = MaxAttempts;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Resets the view model state.
    /// </summary>
    public void Reset()
    {
        EnteredPin = string.Empty;
        ErrorMessage = string.Empty;
        AttemptsRemaining = MaxAttempts;
        IsLockedOut = false;
        LockoutSecondsRemaining = 0;
        IsVerifying = false;
        ShakeError = false;
    }
}
