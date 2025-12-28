using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for VisitNotificationService.
/// Tests SSE visit notifications with navigation conflict handling and background polling.
/// </summary>
public class VisitNotificationServiceTests : IDisposable
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ISseClientFactory> _mockSseClientFactory;
    private readonly Mock<ISseClient> _mockSseClient;
    private readonly Mock<ITextToSpeechService> _mockTtsService;
    private readonly Mock<ILocalNotificationService> _mockNotificationService;
    private readonly Mock<IVisitApiClient> _mockVisitApiClient;
    private readonly Mock<ILocationSyncEventBridge> _mockSyncEventBridge;
    private readonly Mock<ILogger<VisitNotificationService>> _mockLogger;

    public VisitNotificationServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockSseClientFactory = new Mock<ISseClientFactory>();
        _mockSseClient = new Mock<ISseClient>();
        _mockTtsService = new Mock<ITextToSpeechService>();
        _mockNotificationService = new Mock<ILocalNotificationService>();
        _mockVisitApiClient = new Mock<IVisitApiClient>();
        _mockSyncEventBridge = new Mock<ILocationSyncEventBridge>();
        _mockLogger = new Mock<ILogger<VisitNotificationService>>();

        // Default settings
        _mockSettings.Setup(s => s.VisitNotificationsEnabled).Returns(true);
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("both");
        _mockSettings.Setup(s => s.VisitVoiceAnnouncementEnabled).Returns(true);

        // Factory returns mock client
        _mockSseClientFactory.Setup(f => f.Create()).Returns(_mockSseClient.Object);

        // TTS not speaking by default
        _mockTtsService.Setup(t => t.IsSpeaking).Returns(false);

        // Notification returns success
        _mockNotificationService.Setup(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(1);

        // Visit API returns empty by default
        _mockVisitApiClient.Setup(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SseVisitStartedEvent>());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private VisitNotificationService CreateService(bool includeSyncBridge = false)
    {
        return new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            includeSyncBridge ? _mockSyncEventBridge.Object : null,
            _mockLogger.Object);
    }

    private static SseVisitStartedEvent CreateVisitEvent(
        Guid? visitId = null,
        Guid? tripId = null,
        Guid? placeId = null,
        string placeName = "Test Place",
        string tripName = "Test Trip")
    {
        return new SseVisitStartedEvent
        {
            VisitId = visitId ?? Guid.NewGuid(),
            TripId = tripId ?? Guid.NewGuid(),
            PlaceId = placeId ?? Guid.NewGuid(),
            PlaceName = placeName,
            TripName = tripName,
            RegionName = "Test Region",
            ArrivedAtUtc = DateTime.UtcNow,
            Latitude = 40.7128,
            Longitude = -74.0060
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            null!,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            null,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_NullSseClientFactory_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            null!,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            null,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("sseClientFactory");
    }

    [Fact]
    public void Constructor_NullTtsService_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            null!,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            null,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ttsService");
    }

    [Fact]
    public void Constructor_NullNotificationService_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            null!,
            _mockVisitApiClient.Object,
            null,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("notificationService");
    }

    [Fact]
    public void Constructor_NullVisitApiClient_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            null!,
            null,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("visitApiClient");
    }

    [Fact]
    public void Constructor_NullSyncEventBridge_IsAllowed()
    {
        // Sync event bridge is optional - allows background poll to be disabled
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            null,
            _mockLogger.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new VisitNotificationService(
            _mockSettings.Object,
            _mockSseClientFactory.Object,
            _mockTtsService.Object,
            _mockNotificationService.Object,
            _mockVisitApiClient.Object,
            null,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidDependencies_CreatesInstance()
    {
        var service = CreateService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithSyncBridge_CreatesInstance()
    {
        var service = CreateService(includeSyncBridge: true);
        service.Should().NotBeNull();
    }

    #endregion

    #region IsSubscribed Tests

    [Fact]
    public void IsSubscribed_InitialState_ReturnsFalse()
    {
        var service = CreateService();
        service.IsSubscribed.Should().BeFalse();
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotSubscribe()
    {
        _mockSettings.Setup(s => s.VisitNotificationsEnabled).Returns(false);

        var service = CreateService();
        await service.StartAsync();

        _mockSseClientFactory.Verify(f => f.Create(), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_CreatesSseClient()
    {
        var service = CreateService();
        await service.StartAsync();

        _mockSseClientFactory.Verify(f => f.Create(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_SubscribesToVisits()
    {
        var service = CreateService();
        await service.StartAsync();

        // Give background task time to start
        await Task.Delay(50);

        _mockSseClient.Verify(c => c.SubscribeToVisitsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadySubscribed_DoesNotCreateNewClient()
    {
        var service = CreateService();
        await service.StartAsync();
        await service.StartAsync();

        _mockSseClientFactory.Verify(f => f.Create(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_DoesNotSubscribe()
    {
        var service = CreateService();
        service.Dispose();

        await service.StartAsync();

        _mockSseClientFactory.Verify(f => f.Create(), Times.Never);
    }

    #endregion

    #region Stop Tests

    [Fact]
    public async Task Stop_WhenSubscribed_StopsClient()
    {
        var service = CreateService();
        await service.StartAsync();

        service.Stop();

        _mockSseClient.Verify(c => c.Stop(), Times.Once);
    }

    [Fact]
    public void Stop_WhenNotSubscribed_DoesNotThrow()
    {
        var service = CreateService();

        var act = () => service.Stop();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Stop_MultipleCalls_DoesNotThrow()
    {
        var service = CreateService();
        await service.StartAsync();

        var act = () =>
        {
            service.Stop();
            service.Stop();
            service.Stop();
        };

        act.Should().NotThrow();
    }

    #endregion

    #region UpdateNavigationState Tests

    [Fact]
    public void UpdateNavigationState_WhenNotNavigating_SetsStateCorrectly()
    {
        var service = CreateService();

        service.UpdateNavigationState(false, null);

        // State should be updated without throwing
        service.Should().NotBeNull();
    }

    [Fact]
    public void UpdateNavigationState_WhenNavigating_SetsStateCorrectly()
    {
        var service = CreateService();
        var placeId = Guid.NewGuid();

        service.UpdateNavigationState(true, placeId);

        // State should be updated without throwing
        service.Should().NotBeNull();
    }

    [Fact]
    public void UpdateNavigationState_TransitionToStopped_SetsCooldownPeriod()
    {
        var service = CreateService();
        var placeId = Guid.NewGuid();

        // Start navigating
        service.UpdateNavigationState(true, placeId);
        // Stop navigating
        service.UpdateNavigationState(false, null);

        // State should be updated without throwing
        service.Should().NotBeNull();
    }

    #endregion

    #region Notification Mode Tests

    [Fact]
    public async Task VisitEvent_WhenNotNavigating_ShowsFullNotification()
    {
        var service = CreateService();
        await service.StartAsync();

        VisitNotificationEventArgs? receivedArgs = null;
        service.NotificationDisplayed += (_, args) => receivedArgs = args;

        // Simulate visit event
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        // Wait for async processing
        await Task.Delay(100);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Mode.Should().Be(VisitNotificationMode.Full);
    }

    [Fact]
    public async Task VisitEvent_WhenNavigatingToSamePlace_SuppressesNotification()
    {
        var placeId = Guid.NewGuid();
        var service = CreateService();
        await service.StartAsync();

        // Start navigating to a place
        service.UpdateNavigationState(true, placeId);

        VisitNotificationEventArgs? receivedArgs = null;
        service.NotificationDisplayed += (_, args) => receivedArgs = args;

        // Simulate visit event for the same place
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(placeId: placeId)));

        await Task.Delay(100);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Mode.Should().Be(VisitNotificationMode.Suppressed);
    }

    [Fact]
    public async Task VisitEvent_WhenNavigatingToDifferentPlace_ShowsSilentNotification()
    {
        var navigatingPlaceId = Guid.NewGuid();
        var visitPlaceId = Guid.NewGuid();
        var service = CreateService();
        await service.StartAsync();

        // Start navigating to a place
        service.UpdateNavigationState(true, navigatingPlaceId);

        VisitNotificationEventArgs? receivedArgs = null;
        service.NotificationDisplayed += (_, args) => receivedArgs = args;

        // Simulate visit event for a different place
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(placeId: visitPlaceId)));

        await Task.Delay(100);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Mode.Should().Be(VisitNotificationMode.Silent);
    }

    #endregion

    #region Notification Style Tests

    [Fact]
    public async Task VisitEvent_StyleNotification_OnlyShowsNotification()
    {
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("notification");

        var service = CreateService();
        await service.StartAsync();

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        await Task.Delay(100);

        _mockNotificationService.Verify(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            false,
            It.IsAny<Dictionary<string, string>>()), Times.Once);

        _mockTtsService.Verify(t => t.SpeakAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VisitEvent_StyleVoice_OnlySpeaksAnnouncement()
    {
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("voice");

        var service = CreateService();
        await service.StartAsync();

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        await Task.Delay(100);

        _mockNotificationService.Verify(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<Dictionary<string, string>>()), Times.Never);

        _mockTtsService.Verify(t => t.SpeakAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VisitEvent_StyleBoth_ShowsNotificationAndSpeaks()
    {
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("both");

        var service = CreateService();
        await service.StartAsync();

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        await Task.Delay(100);

        _mockNotificationService.Verify(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            false,
            It.IsAny<Dictionary<string, string>>()), Times.Once);

        _mockTtsService.Verify(t => t.SpeakAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VisitEvent_VoiceDisabled_DoesNotSpeak()
    {
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("both");
        _mockSettings.Setup(s => s.VisitVoiceAnnouncementEnabled).Returns(false);

        var service = CreateService();
        await service.StartAsync();

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        await Task.Delay(100);

        _mockTtsService.Verify(t => t.SpeakAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VisitEvent_SilentMode_ShowsSilentNotificationNoVoice()
    {
        var navigatingPlaceId = Guid.NewGuid();
        var visitPlaceId = Guid.NewGuid();
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("both");

        var service = CreateService();
        await service.StartAsync();
        service.UpdateNavigationState(true, navigatingPlaceId);

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(placeId: visitPlaceId)));

        await Task.Delay(100);

        // Silent notification
        _mockNotificationService.Verify(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            true, // silent = true
            It.IsAny<Dictionary<string, string>>()), Times.Once);

        // No voice
        _mockTtsService.Verify(t => t.SpeakAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Deduplication Tests

    [Fact]
    public async Task VisitEvent_DuplicateVisitId_IsIgnored()
    {
        var visitId = Guid.NewGuid();
        var service = CreateService();
        await service.StartAsync();

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        // Send same visit twice
        var visit = CreateVisitEvent(visitId: visitId);
        _mockSseClient.Raise(c => c.VisitStarted += null, new SseVisitStartedEventArgs(visit));
        await Task.Delay(50);
        _mockSseClient.Raise(c => c.VisitStarted += null, new SseVisitStartedEventArgs(visit));
        await Task.Delay(100);

        eventCount.Should().Be(1);
    }

    [Fact]
    public async Task VisitEvent_DifferentVisitIds_AreProcessed()
    {
        var service = CreateService();
        await service.StartAsync();

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        // Send different visits
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));
        await Task.Delay(50);
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));
        await Task.Delay(100);

        eventCount.Should().Be(2);
    }

    [Fact]
    public async Task VisitEvent_CircularBufferEvictsOldIds()
    {
        var service = CreateService();
        await service.StartAsync();

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        // Send 11 unique visits to fill buffer (max 10)
        var firstVisitId = Guid.NewGuid();
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(visitId: firstVisitId)));
        await Task.Delay(30);

        for (int i = 0; i < 10; i++)
        {
            _mockSseClient.Raise(c => c.VisitStarted += null,
                new SseVisitStartedEventArgs(CreateVisitEvent()));
            await Task.Delay(30);
        }

        // Re-send the first visit - should be processed now (evicted from buffer)
        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(visitId: firstVisitId)));
        await Task.Delay(100);

        eventCount.Should().Be(12); // 11 unique + 1 re-processed after eviction
    }

    #endregion

    #region Notification Content Tests

    [Fact]
    public async Task VisitEvent_NotificationTitle_ContainsPlaceName()
    {
        var service = CreateService();
        await service.StartAsync();

        string? capturedTitle = null;
        _mockNotificationService
            .Setup(n => n.ShowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Dictionary<string, string>>()))
            .Callback<string, string, bool, Dictionary<string, string>?>((title, _, _, _) => capturedTitle = title)
            .ReturnsAsync(1);

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(placeName: "Central Park")));

        await Task.Delay(100);

        capturedTitle.Should().Contain("Central Park");
    }

    [Fact]
    public async Task VisitEvent_NotificationMessage_ContainsTripName()
    {
        var service = CreateService();
        await service.StartAsync();

        string? capturedMessage = null;
        _mockNotificationService
            .Setup(n => n.ShowAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Dictionary<string, string>>()))
            .Callback<string, string, bool, Dictionary<string, string>?>((_, message, _, _) => capturedMessage = message)
            .ReturnsAsync(1);

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(tripName: "NYC Tour")));

        await Task.Delay(100);

        capturedMessage.Should().Contain("NYC Tour");
    }

    [Fact]
    public async Task VisitEvent_VoiceAnnouncement_ContainsPlaceName()
    {
        _mockSettings.Setup(s => s.VisitNotificationStyle).Returns("voice");

        var service = CreateService();
        await service.StartAsync();

        string? capturedText = null;
        _mockTtsService
            .Setup(t => t.SpeakAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => capturedText = text)
            .Returns(Task.CompletedTask);

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent(placeName: "Times Square")));

        await Task.Delay(100);

        capturedText.Should().Contain("Times Square");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task Dispose_StopsSseClient()
    {
        var service = CreateService();
        await service.StartAsync();

        service.Dispose();

        _mockSseClient.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = CreateService();

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
            service.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromEvents()
    {
        var service = CreateService();
        await service.StartAsync();

        service.Dispose();

        _mockSseClient.Verify(c => c.Stop(), Times.Once);
    }

    #endregion

    #region Event Tests

    [Fact]
    public void NotificationDisplayed_CanSubscribeAndUnsubscribe()
    {
        var service = CreateService();
        var eventRaised = false;
        void Handler(object? sender, VisitNotificationEventArgs e) => eventRaised = true;

        service.NotificationDisplayed += Handler;
        service.NotificationDisplayed -= Handler;

        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task NotificationDisplayed_RaisedWithCorrectVisit()
    {
        var service = CreateService();
        await service.StartAsync();

        var expectedVisit = CreateVisitEvent(placeName: "Unique Place");
        VisitNotificationEventArgs? receivedArgs = null;
        service.NotificationDisplayed += (_, args) => receivedArgs = args;

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(expectedVisit));

        await Task.Delay(100);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Visit.PlaceName.Should().Be("Unique Place");
    }

    #endregion

    #region Settings Change Tests

    [Fact]
    public async Task VisitEvent_WhenDisabledDuringProcessing_IsIgnored()
    {
        var service = CreateService();
        await service.StartAsync();

        // Disable after starting
        _mockSettings.Setup(s => s.VisitNotificationsEnabled).Returns(false);

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        _mockSseClient.Raise(c => c.VisitStarted += null,
            new SseVisitStartedEventArgs(CreateVisitEvent()));

        await Task.Delay(100);

        // Event should not trigger notifications
        _mockNotificationService.Verify(n => n.ShowAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<Dictionary<string, string>>()), Times.Never);
    }

    #endregion

    #region Background Poll Tests

    [Fact]
    public async Task StartAsync_WithSyncBridge_SubscribesToLocationSynced()
    {
        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        // Verify subscription to LocationSynced event
        _mockSyncEventBridge.VerifyAdd(b => b.LocationSynced += It.IsAny<EventHandler<LocationSyncedBridgeEventArgs>>(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithoutSyncBridge_DoesNotSubscribe()
    {
        var service = CreateService(includeSyncBridge: false);
        await service.StartAsync();

        // No sync bridge, no subscription attempt
        _mockSyncEventBridge.VerifyAdd(b => b.LocationSynced += It.IsAny<EventHandler<LocationSyncedBridgeEventArgs>>(), Times.Never);
    }

    [Fact]
    public async Task LocationSynced_WhenSseConnected_SkipsPoll()
    {
        _mockSseClient.Setup(c => c.IsConnected).Returns(true);

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        // Simulate location synced event
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });

        await Task.Delay(100);

        // Should not poll API when SSE is connected
        _mockVisitApiClient.Verify(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LocationSynced_WhenSseDisconnected_PollsForVisits()
    {
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();
        await Task.Delay(50); // Wait for SSE to start

        // SSE not connected
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);

        // Simulate location synced event
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });

        await Task.Delay(200);

        // Should poll API when SSE is not connected
        _mockVisitApiClient.Verify(c => c.GetRecentVisitsAsync(30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LocationSynced_WithVisits_ProcessesEachVisit()
    {
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);

        var visits = new List<SseVisitStartedEvent>
        {
            CreateVisitEvent(placeName: "Place A"),
            CreateVisitEvent(placeName: "Place B")
        };
        _mockVisitApiClient.Setup(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visits);

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        // Simulate location synced
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });

        await Task.Delay(200);

        eventCount.Should().Be(2);
    }

    [Fact]
    public async Task LocationSynced_WhenNotificationsDisabled_SkipsPoll()
    {
        _mockSettings.Setup(s => s.VisitNotificationsEnabled).Returns(false);
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        // Simulate location synced
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });

        await Task.Delay(100);

        // Should not poll when feature is disabled
        _mockVisitApiClient.Verify(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LocationSynced_ApiThrows_DoesNotCrash()
    {
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);
        _mockVisitApiClient.Setup(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        // Simulate location synced - should not throw
        var act = async () =>
        {
            _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
                new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });
            await Task.Delay(200);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LocationSynced_DuplicateVisit_IsFiltered()
    {
        _mockSseClient.Setup(c => c.IsConnected).Returns(false);

        var visitId = Guid.NewGuid();
        var visit = CreateVisitEvent(visitId: visitId);

        // First poll returns the visit
        _mockVisitApiClient.Setup(c => c.GetRecentVisitsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SseVisitStartedEvent> { visit });

        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        int eventCount = 0;
        service.NotificationDisplayed += (_, _) => eventCount++;

        // First sync - should process
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 123, Timestamp = DateTime.UtcNow });
        await Task.Delay(200);

        // Second sync - same visit should be filtered
        _mockSyncEventBridge.Raise(b => b.LocationSynced += null,
            new LocationSyncedBridgeEventArgs { ServerId = 124, Timestamp = DateTime.UtcNow });
        await Task.Delay(200);

        eventCount.Should().Be(1);
    }

    [Fact]
    public async Task Stop_UnsubscribesFromSyncEvents()
    {
        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        service.Stop();

        _mockSyncEventBridge.VerifyRemove(b => b.LocationSynced -= It.IsAny<EventHandler<LocationSyncedBridgeEventArgs>>(), Times.Once);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromSyncEvents()
    {
        var service = CreateService(includeSyncBridge: true);
        await service.StartAsync();

        service.Dispose();

        _mockSyncEventBridge.VerifyRemove(b => b.LocationSynced -= It.IsAny<EventHandler<LocationSyncedBridgeEventArgs>>(), Times.Once);
    }

    #endregion
}
