using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for monitoring battery status and adjusting tracking behavior accordingly.
/// </summary>
public class BatteryMonitorService : IDisposable
{
    private readonly ILogger<BatteryMonitorService> _logger;
    private readonly SettingsService _settingsService;
    private readonly IToastService _toastService;
    private bool _disposed;
    private bool _lowBatteryWarningShown;
    private bool _criticalBatteryWarningShown;

    /// <summary>
    /// Low battery threshold (20%).
    /// </summary>
    public const double LowBatteryThreshold = 0.20;

    /// <summary>
    /// Critical battery threshold (10%).
    /// </summary>
    public const double CriticalBatteryThreshold = 0.10;

    /// <summary>
    /// Event raised when battery level becomes low.
    /// </summary>
    public event EventHandler<BatteryLevelEventArgs>? BatteryLow;

    /// <summary>
    /// Event raised when battery level becomes critical.
    /// </summary>
    public event EventHandler<BatteryLevelEventArgs>? BatteryCritical;

    /// <summary>
    /// Event raised when energy saver mode changes.
    /// </summary>
    public event EventHandler<bool>? EnergySaverChanged;

    /// <summary>
    /// Initializes a new instance of the BatteryMonitorService class.
    /// </summary>
    public BatteryMonitorService(
        ILogger<BatteryMonitorService> logger,
        SettingsService settingsService,
        IToastService toastService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _toastService = toastService;

        // Subscribe to battery events
        Battery.Default.BatteryInfoChanged += OnBatteryInfoChanged;
        Battery.Default.EnergySaverStatusChanged += OnEnergySaverStatusChanged;
    }

    /// <summary>
    /// Gets the current battery level (0.0 - 1.0).
    /// </summary>
    public double BatteryLevel => Battery.Default.ChargeLevel;

    /// <summary>
    /// Gets the current battery state.
    /// </summary>
    public BatteryState BatteryState => Battery.Default.State;

    /// <summary>
    /// Gets the current power source.
    /// </summary>
    public BatteryPowerSource PowerSource => Battery.Default.PowerSource;

    /// <summary>
    /// Gets whether energy saver mode is enabled.
    /// </summary>
    public bool IsEnergySaverOn => Battery.Default.EnergySaverStatus == EnergySaverStatus.On;

    /// <summary>
    /// Gets whether the battery is currently charging.
    /// </summary>
    public bool IsCharging => BatteryState == BatteryState.Charging || BatteryState == BatteryState.Full;

    /// <summary>
    /// Gets whether the battery is low (below 20%).
    /// </summary>
    public bool IsLowBattery => BatteryLevel <= LowBatteryThreshold && !IsCharging;

    /// <summary>
    /// Gets whether the battery is critical (below 10%).
    /// </summary>
    public bool IsCriticalBattery => BatteryLevel <= CriticalBatteryThreshold && !IsCharging;

    /// <summary>
    /// Gets whether tracking should continue based on battery status.
    /// Returns false if battery is critical and not charging.
    /// </summary>
    public bool ShouldContinueTracking
    {
        get
        {
            // If auto-pause on critical battery is enabled
            if (_settingsService.AutoPauseTrackingOnCriticalBattery && IsCriticalBattery)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Gets a human-readable battery status string.
    /// </summary>
    public string StatusText
    {
        get
        {
            var level = $"{BatteryLevel:P0}";
            var state = BatteryState switch
            {
                BatteryState.Charging => "Charging",
                BatteryState.Discharging => "Discharging",
                BatteryState.Full => "Full",
                BatteryState.NotCharging => "Not Charging",
                BatteryState.NotPresent => "No Battery",
                _ => "Unknown"
            };

            return $"{level} ({state})";
        }
    }

    /// <summary>
    /// Performs an initial battery check and raises warnings if necessary.
    /// </summary>
    public async Task CheckBatteryStatusAsync()
    {
        if (IsCriticalBattery)
        {
            await ShowCriticalBatteryWarningAsync();
        }
        else if (IsLowBattery)
        {
            await ShowLowBatteryWarningAsync();
        }
    }

    /// <summary>
    /// Resets the warning flags so warnings can be shown again.
    /// </summary>
    public void ResetWarnings()
    {
        _lowBatteryWarningShown = false;
        _criticalBatteryWarningShown = false;
    }

    private void OnBatteryInfoChanged(object? sender, BatteryInfoChangedEventArgs e)
    {
        _logger.LogDebug("Battery changed: {Level:P0}, State: {State}", e.ChargeLevel, e.State);

        // Reset warnings when charging
        if (e.State == BatteryState.Charging || e.State == BatteryState.Full)
        {
            _lowBatteryWarningShown = false;
            _criticalBatteryWarningShown = false;
            return;
        }

        // Check for critical battery
        if (e.ChargeLevel <= CriticalBatteryThreshold && !_criticalBatteryWarningShown)
        {
            BatteryCritical?.Invoke(this, new BatteryLevelEventArgs(e.ChargeLevel, e.State));
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await ShowCriticalBatteryWarningAsync();
            });
        }
        // Check for low battery
        else if (e.ChargeLevel <= LowBatteryThreshold && !_lowBatteryWarningShown)
        {
            BatteryLow?.Invoke(this, new BatteryLevelEventArgs(e.ChargeLevel, e.State));
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await ShowLowBatteryWarningAsync();
            });
        }
    }

    private void OnEnergySaverStatusChanged(object? sender, EnergySaverStatusChangedEventArgs e)
    {
        var isOn = e.EnergySaverStatus == EnergySaverStatus.On;
        _logger.LogInformation("Energy saver mode: {IsOn}", isOn);

        EnergySaverChanged?.Invoke(this, isOn);

        if (isOn && _settingsService.TimelineTrackingEnabled)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await _toastService.ShowWarningAsync("Energy Saver mode may affect location tracking accuracy");
            });
        }
    }

    private async Task ShowLowBatteryWarningAsync()
    {
        if (_lowBatteryWarningShown) return;

        _lowBatteryWarningShown = true;
        _logger.LogWarning("Battery low: {Level:P0}", BatteryLevel);

        if (_settingsService.TimelineTrackingEnabled)
        {
            await _toastService.ShowWarningAsync($"Battery low ({BatteryLevel:P0}). Tracking will continue but consider charging.");
        }
    }

    private async Task ShowCriticalBatteryWarningAsync()
    {
        if (_criticalBatteryWarningShown) return;

        _criticalBatteryWarningShown = true;
        _logger.LogWarning("Battery critical: {Level:P0}", BatteryLevel);

        if (_settingsService.TimelineTrackingEnabled)
        {
            if (_settingsService.AutoPauseTrackingOnCriticalBattery)
            {
                await _toastService.ShowErrorAsync($"Battery critical ({BatteryLevel:P0}). Tracking paused to save battery.");
            }
            else
            {
                await _toastService.ShowWarningAsync($"Battery critical ({BatteryLevel:P0}). Consider pausing tracking.");
            }
        }
    }

    /// <summary>
    /// Disposes the service and unsubscribes from battery events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        Battery.Default.BatteryInfoChanged -= OnBatteryInfoChanged;
        Battery.Default.EnergySaverStatusChanged -= OnEnergySaverStatusChanged;
        _disposed = true;
    }
}

/// <summary>
/// Event arguments for battery level events.
/// </summary>
public class BatteryLevelEventArgs : EventArgs
{
    /// <summary>
    /// Gets the battery charge level (0.0 - 1.0).
    /// </summary>
    public double ChargeLevel { get; }

    /// <summary>
    /// Gets the battery state.
    /// </summary>
    public BatteryState State { get; }

    /// <summary>
    /// Initializes a new instance of the BatteryLevelEventArgs class.
    /// </summary>
    public BatteryLevelEventArgs(double chargeLevel, BatteryState state)
    {
        ChargeLevel = chargeLevel;
        State = state;
    }
}
