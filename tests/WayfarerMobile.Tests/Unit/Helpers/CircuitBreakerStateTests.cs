using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for <see cref="CircuitBreakerState"/>.
/// See #216: the circuit breaker prevents repeated slow timeouts when the
/// server is known to be unreachable, since MAUI Connectivity only checks
/// device network adapter status, not actual server reachability.
/// </summary>
public class CircuitBreakerStateTests
{
    #region Initial State

    [Fact]
    public void NewCircuitBreaker_IsClosed()
    {
        var cb = new CircuitBreakerState();

        cb.IsOpen.Should().BeFalse();
        cb.ConsecutiveFailures.Should().Be(0);
    }

    #endregion

    #region Opening the Circuit

    [Fact]
    public void BelowThreshold_CircuitStaysClosed()
    {
        var cb = new CircuitBreakerState(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeFalse();
        cb.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void AtThreshold_CircuitOpens()
    {
        var cb = new CircuitBreakerState(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        cb.IsOpen.Should().BeTrue();
        cb.ConsecutiveFailures.Should().Be(3);
    }

    [Fact]
    public void AboveThreshold_CircuitStaysOpen()
    {
        var cb = new CircuitBreakerState(threshold: 3);

        for (var i = 0; i < 10; i++)
            cb.RecordFailure();

        cb.IsOpen.Should().BeTrue();
        cb.ConsecutiveFailures.Should().Be(10);
    }

    #endregion

    #region Closing the Circuit

    [Fact]
    public void RecordSuccess_ClosesOpenCircuit()
    {
        var cb = new CircuitBreakerState(threshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();

        var wasClosed = cb.RecordSuccess();

        wasClosed.Should().BeTrue();
        cb.IsOpen.Should().BeFalse();
        cb.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordSuccess_WhenAlreadyClosed_ReturnsFalse()
    {
        var cb = new CircuitBreakerState();

        var wasClosed = cb.RecordSuccess();

        wasClosed.Should().BeFalse();
    }

    [Fact]
    public void RecordSuccess_ResetsCounterCompletely()
    {
        var cb = new CircuitBreakerState(threshold: 3);

        // Open circuit
        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordFailure();

        // Close it
        cb.RecordSuccess();

        // One failure should not reopen (counter was fully reset)
        cb.RecordFailure();
        cb.IsOpen.Should().BeFalse();
    }

    #endregion

    #region Half-Open (Cooldown)

    [Fact]
    public void AfterCooldown_CircuitHalfOpens()
    {
        // Use a very short cooldown for testability
        var cb = new CircuitBreakerState(threshold: 1, cooldown: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();

        // Wait for cooldown to expire
        Thread.Sleep(100);

        // Circuit should be half-open (IsOpen returns false to allow probe)
        cb.IsOpen.Should().BeFalse();
        // But failure count is still above threshold
        cb.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public void AfterCooldownAndNewFailure_CircuitReopens()
    {
        var cb = new CircuitBreakerState(threshold: 1, cooldown: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        Thread.Sleep(100);
        cb.IsOpen.Should().BeFalse(); // half-open

        // Probe fails — circuit should reopen
        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void AfterCooldownAndSuccess_CircuitCloses()
    {
        var cb = new CircuitBreakerState(threshold: 1, cooldown: TimeSpan.FromMilliseconds(50));

        cb.RecordFailure();
        Thread.Sleep(100);
        cb.IsOpen.Should().BeFalse(); // half-open

        // Probe succeeds — circuit should fully close
        cb.RecordSuccess();
        cb.ConsecutiveFailures.Should().Be(0);
        cb.IsOpen.Should().BeFalse();
    }

    #endregion

    #region Custom Configuration

    [Fact]
    public void CustomThreshold_IsRespected()
    {
        var cb = new CircuitBreakerState(threshold: 5);

        for (var i = 0; i < 4; i++)
            cb.RecordFailure();

        cb.IsOpen.Should().BeFalse();

        cb.RecordFailure();
        cb.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var cb = new CircuitBreakerState();

        cb.Threshold.Should().Be(3);
        cb.Cooldown.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    #region Thread Safety

    [Fact]
    public void ConcurrentFailures_AreCountedCorrectly()
    {
        var cb = new CircuitBreakerState(threshold: 100);

        // Hammer the circuit breaker from multiple threads
        Parallel.For(0, 100, _ => cb.RecordFailure());

        cb.ConsecutiveFailures.Should().Be(100);
    }

    [Fact]
    public void ConcurrentSuccessAndFailure_DoNotCorrupt()
    {
        var cb = new CircuitBreakerState(threshold: 1000);

        // Interleave failures and successes — should not throw or corrupt state
        Parallel.For(0, 1000, i =>
        {
            if (i % 3 == 0)
                cb.RecordSuccess();
            else
                cb.RecordFailure();
        });

        // State should be consistent (no negative values, etc.)
        cb.ConsecutiveFailures.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion
}
