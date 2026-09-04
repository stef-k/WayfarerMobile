using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Services;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Tests owner isolation around the single physical wake lock.</summary>
public sealed class WakeLockOwnershipTests
{
    [Theory]
    [InlineData(WakeLockOwner.Persistent, WakeLockOwner.Navigation)]
    [InlineData(WakeLockOwner.Navigation, WakeLockOwner.Persistent)]
    public void OverlappingOwners_ReleaseIndependently(WakeLockOwner first, WakeLockOwner second)
    {
        var ownership = new WakeLockOwnership();
        var acquisitions = 0;
        var releases = 0;

        ownership.TryAcquire(first, () => { acquisitions++; return true; }).Should().BeTrue();
        ownership.TryAcquire(second, () => { acquisitions++; return true; }).Should().BeTrue();
        ownership.Release(first, () => { releases++; return true; });

        ownership.IsHeld.Should().BeTrue();
        acquisitions.Should().Be(1);
        releases.Should().Be(0);

        ownership.Release(second, () => { releases++; return true; });
        ownership.IsHeld.Should().BeFalse();
        releases.Should().Be(1);
    }

    [Fact]
    public void FailedFirstAcquisition_DoesNotRecordClaimOrRelease()
    {
        var ownership = new WakeLockOwnership();
        var releases = 0;

        ownership.TryAcquire(WakeLockOwner.Navigation, () => false).Should().BeFalse();
        ownership.Release(WakeLockOwner.Navigation, () => { releases++; return true; });

        ownership.IsHeld.Should().BeFalse();
        releases.Should().Be(0);
    }

    [Fact]
    public void RepeatedSameOwner_IsIdempotent()
    {
        var ownership = new WakeLockOwnership();
        var acquisitions = 0;
        var releases = 0;

        for (var index = 0; index < 3; index++)
            ownership.TryAcquire(WakeLockOwner.Navigation, () => { acquisitions++; return true; }).Should().BeTrue();
        ownership.Release(WakeLockOwner.Navigation, () => { releases++; return true; });
        ownership.Release(WakeLockOwner.Navigation, () => { releases++; return true; });

        acquisitions.Should().Be(1);
        releases.Should().Be(1);
        ownership.IsHeld.Should().BeFalse();
    }

    [Fact]
    public void RepeatedAcquireReleaseCycles_DoNotLeakOrDoubleRelease()
    {
        var ownership = new WakeLockOwnership();
        var acquisitions = 0;
        var releases = 0;

        for (var index = 0; index < 3; index++)
        {
            ownership.TryAcquire(WakeLockOwner.Navigation,
                () => { acquisitions++; return true; }).Should().BeTrue();
            ownership.Release(WakeLockOwner.Navigation,
                () => { releases++; return true; });
        }

        acquisitions.Should().Be(3);
        releases.Should().Be(3);
        ownership.IsHeld.Should().BeFalse();
    }

    [Fact]
    public void PhysicalCallbacksThrow_OwnershipStateRemainsConsistent()
    {
        var ownership = new WakeLockOwnership();
        var releases = 0;

        ownership.TryAcquire(WakeLockOwner.Navigation,
            () => throw new InvalidOperationException()).Should().BeFalse();
        ownership.IsHeld.Should().BeFalse();

        ownership.TryAcquire(WakeLockOwner.Persistent, () => true).Should().BeTrue();
        ownership.Release(WakeLockOwner.Persistent,
            () => throw new InvalidOperationException());
        ownership.IsHeld.Should().BeTrue();

        ownership.Release(WakeLockOwner.Persistent, () => { releases++; return true; });
        releases.Should().Be(1);
        ownership.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentOwners_PreserveOnePhysicalLifetime()
    {
        var ownership = new WakeLockOwnership();
        var acquisitions = 0;
        var releases = 0;

        await Task.WhenAll(Enum.GetValues<WakeLockOwner>().Select(owner => Task.Run(() =>
            ownership.TryAcquire(owner, () => { Interlocked.Increment(ref acquisitions); return true; }))));
        await Task.WhenAll(Enum.GetValues<WakeLockOwner>().Select(owner => Task.Run(() =>
            ownership.Release(owner, () => { Interlocked.Increment(ref releases); return true; }))));

        acquisitions.Should().Be(1);
        releases.Should().Be(1);
        ownership.IsHeld.Should().BeFalse();
    }

    [Fact]
    public void FailedFinalRelease_RetainsClaimForSuccessfulRetry()
    {
        var ownership = new WakeLockOwnership();
        var releases = 0;
        ownership.TryAcquire(WakeLockOwner.Persistent, () => true).Should().BeTrue();

        ownership.Release(WakeLockOwner.Persistent, () => { releases++; return false; });
        ownership.IsHeld.Should().BeTrue();

        ownership.Release(WakeLockOwner.Persistent, () => { releases++; return true; });
        releases.Should().Be(2);
        ownership.IsHeld.Should().BeFalse();
    }

    [Fact]
    public void DifferentOwnerAfterFailedRelease_DoesNotReplaceRetainedClaim()
    {
        var ownership = new WakeLockOwnership();
        var acquisitions = 0;
        var releases = 0;
        ownership.TryAcquire(WakeLockOwner.Persistent,
            () => { acquisitions++; return true; }).Should().BeTrue();
        ownership.Release(WakeLockOwner.Persistent, () => false);

        ownership.TryAcquire(WakeLockOwner.Navigation,
            () => { acquisitions++; return true; }).Should().BeTrue();
        ownership.Release(WakeLockOwner.Navigation,
            () => { releases++; return true; });

        acquisitions.Should().Be(1);
        releases.Should().Be(0);
        ownership.IsHeld.Should().BeTrue();

        ownership.Release(WakeLockOwner.Persistent,
            () => { releases++; return true; });
        releases.Should().Be(1);
        ownership.IsHeld.Should().BeFalse();
    }
}
