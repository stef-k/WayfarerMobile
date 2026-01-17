using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

/// <summary>
/// Provides pulse animation timing for live group member markers.
/// Uses a periodic timer to calculate scale values that create a pulsing effect.
/// </summary>
/// <remarks>
/// The pulse animation cycles through scale values to simulate the server's CSS animation:
/// - Scale range: 1.0 to 1.35 (same as server's pulse-centered keyframes)
/// - Cycle duration: 1.6 seconds (same as server)
/// </remarks>
public class MarkerPulseAnimator : IDisposable
{
    private readonly ILogger<MarkerPulseAnimator> _logger;
    private readonly System.Timers.Timer _timer;
    private readonly object _lock = new();

    private bool _isRunning;
    private bool _disposed;
    private DateTime _startTime;

    private const double CycleDurationMs = 1600; // 1.6 seconds like server
    private const double MinScale = 1.0;
    private const double MaxScale = 1.35;
    private const int UpdateIntervalMs = 50; // 20 FPS for smooth animation

    /// <summary>
    /// Event raised when the pulse scale changes.
    /// Subscribers should refresh their map markers with the new scale.
    /// </summary>
    public event EventHandler<double>? PulseScaleChanged;

    /// <summary>
    /// Gets the current pulse scale (1.0 to 1.35).
    /// </summary>
    public double CurrentScale { get; private set; } = MinScale;

    /// <summary>
    /// Gets whether the animator is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Creates a new instance of MarkerPulseAnimator.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MarkerPulseAnimator(ILogger<MarkerPulseAnimator> logger)
    {
        _logger = logger;
        _timer = new System.Timers.Timer(UpdateIntervalMs);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    /// <summary>
    /// Starts the pulse animation.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning || _disposed)
                return;

            _startTime = DateTime.UtcNow;
            _isRunning = true;
            _timer.Start();
            _logger.LogDebug("Pulse animator started");
        }
    }

    /// <summary>
    /// Stops the pulse animation.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return;

            _timer.Stop();
            _isRunning = false;
            CurrentScale = MinScale;
            _logger.LogDebug("Pulse animator stopped");
        }
    }

    /// <summary>
    /// Handles the timer elapsed event.
    /// </summary>
    private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_lock)
        {
            if (!_isRunning || _disposed)
                return;

            // Calculate elapsed time in the current cycle
            var elapsed = (DateTime.UtcNow - _startTime).TotalMilliseconds;
            var cyclePosition = (elapsed % CycleDurationMs) / CycleDurationMs;

            // Calculate scale using a sine wave for smooth in/out
            // sin goes from 0 to 1 to 0 over a full cycle when we use sin(position * PI)
            var sineValue = Math.Sin(cyclePosition * Math.PI);
            CurrentScale = MinScale + (MaxScale - MinScale) * sineValue;
        }

        // Raise event on main thread for UI updates
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PulseScaleChanged?.Invoke(this, CurrentScale);
        });
    }

    /// <summary>
    /// Disposes the animator and stops the timer.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
            _isRunning = false;
        }

        GC.SuppressFinalize(this);
    }
}
