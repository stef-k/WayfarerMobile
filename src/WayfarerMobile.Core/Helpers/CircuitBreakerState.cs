namespace WayfarerMobile.Core.Helpers;

/// <summary>
/// Lightweight circuit breaker that tracks consecutive API failures to short-circuit
/// calls when a server is known to be unreachable.
/// Thread-safe: uses <see cref="Interlocked"/> for all mutable state.
/// </summary>
/// <remarks>
/// Created for #216: MAUI <c>Connectivity.NetworkAccess</c> reports "Internet" when the device
/// has WiFi/cellular, regardless of whether the target server is actually reachable.
/// This circuit breaker provides actual server reachability tracking.
/// </remarks>
public class CircuitBreakerState
{
    private int _consecutiveFailures;
    private long _lastFailureUtcTicks = DateTime.MinValue.Ticks;

    /// <summary>
    /// Number of consecutive failures before the circuit opens (skips server calls).
    /// </summary>
    public int Threshold { get; }

    /// <summary>
    /// Time after the last failure before the circuit half-opens to allow a probe request.
    /// </summary>
    public TimeSpan Cooldown { get; }

    /// <summary>
    /// Creates a new circuit breaker state.
    /// </summary>
    /// <param name="threshold">Number of consecutive failures to open the circuit.</param>
    /// <param name="cooldown">Duration after last failure before half-opening.</param>
    public CircuitBreakerState(int threshold = 3, TimeSpan? cooldown = null)
    {
        Threshold = threshold;
        Cooldown = cooldown ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Whether the circuit is open (server known unreachable).
    /// Returns false after the cooldown period expires, allowing a probe request (half-open).
    /// </summary>
    public bool IsOpen =>
        Volatile.Read(ref _consecutiveFailures) >= Threshold
        && (DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastFailureUtcTicks), DateTimeKind.Utc)) < Cooldown;

    /// <summary>
    /// Current consecutive failure count.
    /// </summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// Records a successful call, closing the circuit.
    /// </summary>
    /// <returns>True if the circuit was previously open and is now closed.</returns>
    public bool RecordSuccess()
    {
        if (Volatile.Read(ref _consecutiveFailures) > 0)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Records a failed call, incrementing the failure counter.
    /// </summary>
    /// <returns>The new consecutive failure count.</returns>
    public int RecordFailure()
    {
        Interlocked.Exchange(ref _lastFailureUtcTicks, DateTime.UtcNow.Ticks);
        return Interlocked.Increment(ref _consecutiveFailures);
    }
}
