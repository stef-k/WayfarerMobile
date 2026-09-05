using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class AuthenticationStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_AfterContextBoundPreload_CompletesWithoutPumpingCallerContext(bool legacy)
    {
        var envelopeRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new HeldContext();
        var store = new Mock<IProtectedAuthenticationStore>();
        store.Setup(x => x.GetAsync(CommittedAuthenticationAuthority.EnvelopeKey)).Returns(envelopeRead.Task);
        store.Setup(x => x.GetAsync(CommittedAuthenticationAuthority.LegacyServerKey))
            .ReturnsAsync("https://wayfarer.test");
        store.Setup(x => x.GetAsync(CommittedAuthenticationAuthority.LegacyTokenKey)).ReturnsAsync("test-token");
        store.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var authority = new CommittedAuthenticationAuthority(store.Object,
            NullLogger<CommittedAuthenticationAuthority>.Instance);
        Task preload = Task.CompletedTask;

        // App starts pipeline preload on its context, then synchronously reads IsConfigured
        // through activity sync before returning to the message loop. Hold storage until
        // preload has acquired the gate, so this ordering does not depend on device speed.
        var current = Task.Factory.StartNew(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                preload = authority.PreloadAsync();
                preloadStarted.SetResult();
                return authority.Current;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await preloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            envelopeRead.SetResult(legacy ? null :
                """{"ServerUrl":"https://wayfarer.test","ApiToken":"test-token","RoutingPartition":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""");

            // A posted continuation cannot run while this context's thread reads Current.
            // The timeout is only a test watchdog; the regression is detected by Post.
            var completed = await Task.WhenAny(current, context.Posted.Task).WaitAsync(TimeSpan.FromSeconds(10));
            completed.Should().BeSameAs(current, "authentication loading must release its gate without the blocked caller context");
            var snapshot = await current;
            snapshot.ServerUrl.Should().Be("https://wayfarer.test");
            snapshot.ApiToken.Should().Be("test-token");
            snapshot.RoutingPartition.Should().NotBe(Guid.Empty);
            if (!legacy)
                snapshot.RoutingPartition.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            authority.Revision.Should().Be(1);
            store.Verify(x => x.GetAsync(CommittedAuthenticationAuthority.EnvelopeKey), Times.Once);
        }
        finally
        {
            // Rescue the deliberately blocked baseline ordering, including future posts,
            // so a failing regression leaves no stranded worker or owned semaphore.
            envelopeRead.TrySetResult(null);
            context.Release();
            await current.WaitAsync(TimeSpan.FromSeconds(10));
            await preload.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class HeldContext : SynchronizationContext
    {
        private readonly object gate = new();
        private readonly List<Action> callbacks = new();
        private bool released;
        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (gate)
            {
                if (released) Task.Run(() => callback(state));
                else callbacks.Add(() => callback(state));
                Posted.TrySetResult();
            }
        }

        public void Release()
        {
            lock (gate)
            {
                released = true;
                foreach (var callback in callbacks) Task.Run(callback);
                callbacks.Clear();
            }
        }
    }
}
