using WayfarerMobile.Core.Utilities;

namespace WayfarerMobile.Tests.Unit.Utilities;

/// <summary>
/// Unit tests for CompositeDisposable.
/// Verifies proper disposal ordering, thread safety, and edge cases.
/// </summary>
public class CompositeDisposableTests
{
    #region Add and Dispose Tests

    [Fact]
    public void Add_SingleDisposable_DisposesOnDispose()
    {
        // Arrange
        var disposed = false;
        var disposable = new TestDisposable(() => disposed = true);
        var composite = new CompositeDisposable();

        // Act
        composite.Add(disposable);
        composite.Dispose();

        // Assert
        disposed.Should().BeTrue();
    }

    [Fact]
    public void Add_MultipleDisposables_AllDisposed()
    {
        // Arrange
        var disposedCount = 0;
        var composite = new CompositeDisposable();

        // Act
        composite.Add(new TestDisposable(() => disposedCount++));
        composite.Add(new TestDisposable(() => disposedCount++));
        composite.Add(new TestDisposable(() => disposedCount++));
        composite.Dispose();

        // Assert
        disposedCount.Should().Be(3);
    }

    [Fact]
    public void Dispose_DisposesInReverseOrder()
    {
        // Arrange
        var order = new List<int>();
        var composite = new CompositeDisposable();

        // Act
        composite.Add(new TestDisposable(() => order.Add(1)));
        composite.Add(new TestDisposable(() => order.Add(2)));
        composite.Add(new TestDisposable(() => order.Add(3)));
        composite.Dispose();

        // Assert - LIFO order
        order.Should().Equal(3, 2, 1);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var disposedCount = 0;
        var composite = new CompositeDisposable();
        composite.Add(new TestDisposable(() => disposedCount++));

        // Act
        composite.Dispose();
        composite.Dispose();
        composite.Dispose();

        // Assert - only disposed once
        disposedCount.Should().Be(1);
    }

    [Fact]
    public void Add_AfterDispose_DisposesImmediately()
    {
        // Arrange
        var disposed = false;
        var composite = new CompositeDisposable();
        composite.Dispose();

        // Act
        composite.Add(new TestDisposable(() => disposed = true));

        // Assert - disposed immediately
        disposed.Should().BeTrue();
    }

    #endregion

    #region Action Convenience Tests

    [Fact]
    public void AddAction_ExecutesOnDispose()
    {
        // Arrange
        var executed = false;
        var composite = new CompositeDisposable();

        // Act
        composite.Add(() => executed = true);
        composite.Dispose();

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public void AddAction_ExecutesInReverseOrder()
    {
        // Arrange
        var order = new List<string>();
        var composite = new CompositeDisposable();

        // Act
        composite.Add(() => order.Add("first"));
        composite.Add(() => order.Add("second"));
        composite.Add(() => order.Add("third"));
        composite.Dispose();

        // Assert
        order.Should().Equal("third", "second", "first");
    }

    [Fact]
    public void AddAction_EventUnsubscriptionPattern()
    {
        // Arrange - simulate event subscription pattern
        var eventSource = new TestEventSource();
        var eventReceived = false;
        EventHandler handler = (_, _) => eventReceived = true;
        var composite = new CompositeDisposable();

        // Subscribe and add cleanup
        eventSource.TestEvent += handler;
        composite.Add(() => eventSource.TestEvent -= handler);

        // Verify subscribed
        eventSource.RaiseEvent();
        eventReceived.Should().BeTrue();

        // Act - dispose (unsubscribes)
        eventReceived = false;
        composite.Dispose();

        // Assert - no longer receiving events
        eventSource.RaiseEvent();
        eventReceived.Should().BeFalse();
    }

    #endregion

    #region Count and IsDisposed Tests

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        // Arrange
        var composite = new CompositeDisposable();

        // Act & Assert
        composite.Count.Should().Be(0);

        composite.Add(new TestDisposable(() => { }));
        composite.Count.Should().Be(1);

        composite.Add(new TestDisposable(() => { }));
        composite.Count.Should().Be(2);
    }

    [Fact]
    public void Count_AfterDispose_ReturnsZero()
    {
        // Arrange
        var composite = new CompositeDisposable();
        composite.Add(new TestDisposable(() => { }));
        composite.Add(new TestDisposable(() => { }));

        // Act
        composite.Dispose();

        // Assert
        composite.Count.Should().Be(0);
    }

    [Fact]
    public void IsDisposed_InitiallyFalse()
    {
        // Arrange
        var composite = new CompositeDisposable();

        // Assert
        composite.IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void IsDisposed_AfterDispose_ReturnsTrue()
    {
        // Arrange
        var composite = new CompositeDisposable();

        // Act
        composite.Dispose();

        // Assert
        composite.IsDisposed.Should().BeTrue();
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_ExistingDisposable_ReturnsTrue()
    {
        // Arrange
        var disposable = new TestDisposable(() => { });
        var composite = new CompositeDisposable();
        composite.Add(disposable);

        // Act
        var result = composite.Remove(disposable);

        // Assert
        result.Should().BeTrue();
        composite.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_NonExistingDisposable_ReturnsFalse()
    {
        // Arrange
        var composite = new CompositeDisposable();
        var disposable = new TestDisposable(() => { });

        // Act
        var result = composite.Remove(disposable);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_AfterDispose_ReturnsFalse()
    {
        // Arrange
        var disposable = new TestDisposable(() => { });
        var composite = new CompositeDisposable();
        composite.Add(disposable);
        composite.Dispose();

        // Act
        var result = composite.Remove(disposable);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Remove_DoesNotDisposeRemoved()
    {
        // Arrange
        var disposed = false;
        var disposable = new TestDisposable(() => disposed = true);
        var composite = new CompositeDisposable();
        composite.Add(disposable);

        // Act
        composite.Remove(disposable);
        composite.Dispose();

        // Assert - removed disposable was not disposed
        disposed.Should().BeFalse();
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_DisposesAllAndAllowsReuse()
    {
        // Arrange
        var disposed1 = false;
        var disposed2 = false;
        var composite = new CompositeDisposable();
        composite.Add(new TestDisposable(() => disposed1 = true));
        composite.Add(new TestDisposable(() => disposed2 = true));

        // Act
        composite.Clear();

        // Assert
        disposed1.Should().BeTrue();
        disposed2.Should().BeTrue();
        composite.Count.Should().Be(0);
        composite.IsDisposed.Should().BeFalse(); // Can still be reused
    }

    [Fact]
    public void Clear_AfterDispose_DoesNothing()
    {
        // Arrange
        var composite = new CompositeDisposable();
        composite.Dispose();

        // Act - should not throw
        var act = () => composite.Clear();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Clear_CanAddMoreAfterClear()
    {
        // Arrange
        var disposedFirst = false;
        var disposedSecond = false;
        var composite = new CompositeDisposable();
        composite.Add(new TestDisposable(() => disposedFirst = true));

        // Act
        composite.Clear();
        composite.Add(new TestDisposable(() => disposedSecond = true));
        composite.Dispose();

        // Assert
        disposedFirst.Should().BeTrue();
        disposedSecond.Should().BeTrue();
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public void Dispose_ContinuesAfterException()
    {
        // Arrange
        var disposed1 = false;
        var disposed3 = false;
        var composite = new CompositeDisposable();

        composite.Add(new TestDisposable(() => disposed1 = true));
        composite.Add(new TestDisposable(() => throw new InvalidOperationException("Test")));
        composite.Add(new TestDisposable(() => disposed3 = true));

        // Act
        var act = () => composite.Dispose();

        // Assert - should not throw and all should be attempted
        act.Should().NotThrow();
        disposed1.Should().BeTrue();
        disposed3.Should().BeTrue();
    }

    [Fact]
    public void Add_NullDisposable_ThrowsArgumentNullException()
    {
        // Arrange
        var composite = new CompositeDisposable();

        // Act
        var act = () => composite.Add((IDisposable)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Add_NullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var composite = new CompositeDisposable();

        // Act
        var act = () => composite.Add((Action)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task Add_ConcurrentAdds_AllAreDisposed()
    {
        // Arrange
        var disposedCount = 0;
        var composite = new CompositeDisposable();
        var tasks = new List<Task>();

        // Act - concurrent adds
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                composite.Add(new TestDisposable(() => Interlocked.Increment(ref disposedCount)));
            }));
        }

        await Task.WhenAll(tasks);
        composite.Dispose();

        // Assert
        disposedCount.Should().Be(100);
    }

    [Fact]
    public async Task Dispose_ConcurrentDisposes_OnlyDisposesOnce()
    {
        // Arrange
        var disposedCount = 0;
        var composite = new CompositeDisposable();
        composite.Add(new TestDisposable(() => Interlocked.Increment(ref disposedCount)));
        var tasks = new List<Task>();

        // Act - concurrent disposes
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => composite.Dispose()));
        }

        await Task.WhenAll(tasks);

        // Assert - only disposed once
        disposedCount.Should().Be(1);
    }

    #endregion

    #region Test Helpers

    private class TestDisposable : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public TestDisposable(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose();
        }
    }

    private class TestEventSource
    {
        public event EventHandler? TestEvent;

        public void RaiseEvent()
        {
            TestEvent?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion
}
