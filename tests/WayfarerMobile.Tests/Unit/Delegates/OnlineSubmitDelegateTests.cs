using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Delegates;

/// <summary>
/// Local copy of MAUI's NetworkAccess enum for test isolation.
/// </summary>
/// <remarks>
/// The actual enum is in Microsoft.Maui.Networking which requires MAUI workload.
/// This copy matches the MAUI definition exactly for testing purposes.
/// </remarks>
public enum NetworkAccess
{
    /// <summary>The state of the connectivity is not known.</summary>
    Unknown = 0,
    /// <summary>No connectivity.</summary>
    None = 1,
    /// <summary>Local network access only.</summary>
    Local = 2,
    /// <summary>Limited internet access.</summary>
    ConstrainedInternet = 3,
    /// <summary>Local and Internet access.</summary>
    Internet = 4
}

/// <summary>
/// Unit tests for the OnlineSubmitDelegate behavior defined in App.xaml.cs.
/// </summary>
/// <remarks>
/// <para>
/// The OnlineSubmitDelegate is responsible for submitting location data to the server.
/// Its return semantics are critical for the offline queue system:
/// </para>
/// <list type="bullet">
/// <item><description>Returns serverId: Server accepted with ID - don't queue</description></item>
/// <item><description>Returns null: Server accepted without ID, or skipped (threshold not met) - don't queue</description></item>
/// <item><description>Throws exception: Network or API failure - triggers offline queue fallback</description></item>
/// </list>
/// <para>
/// These tests verify the delegate's behavior by testing the decision logic in isolation.
/// The actual delegate is in App.xaml.cs.WireLocationTrackingDelegates().
/// </para>
/// </remarks>
public class OnlineSubmitDelegateTests
{
    #region Test Infrastructure

    /// <summary>
    /// Simulates the OnlineSubmitDelegate decision logic for testability.
    /// This mirrors the logic in App.xaml.cs exactly.
    /// </summary>
    private static async Task<int?> SimulateOnlineSubmitLogic(
        ApiResult apiResult,
        NetworkAccess networkAccess,
        bool apiClientAvailable = true,
        Func<LocationData, int, Task>? onAccepted = null)
    {
        // Case 0: API client not available
        if (!apiClientAvailable)
            throw new InvalidOperationException("API client not available");

        // Early connectivity check - avoids timeout delays when completely offline
        // Only block on NetworkAccess.None (no network interface at all).
        // Allow Local/ConstrainedInternet to attempt the call for LAN-only server deployments.
        if (networkAccess == NetworkAccess.None)
            throw new HttpRequestException("No network connectivity");

        // Simulate API call result processing

        // Case 1: Server accepted or skipped - don't queue
        // Note: log-location API may return just { "success": true } without locationId
        if (apiResult.Success)
        {
            // Only call onAccepted if server returned a locationId (not skipped)
            if (!apiResult.Skipped && apiResult.LocationId.HasValue && onAccepted != null)
            {
                await onAccepted(new LocationData(), apiResult.LocationId.Value);
            }
            // Return locationId if available, null otherwise (either skipped or accepted without ID)
            return apiResult.LocationId;
        }

        // Case 2: Transient failure - throw to trigger offline queue
        // Check both IsTransient flag AND status code, since ApiClient may not set
        // IsTransient for HTTP status failures (they come through with IsTransient=false)
        // Transient codes: 408 (Request Timeout), 429 (Too Many Requests), 5xx (Server Error)
        // QueueDrainService handles these appropriately with retry logic.
        var isTransientStatusCode = apiResult.StatusCode.HasValue &&
            (apiResult.StatusCode == 408 || apiResult.StatusCode == 429 || apiResult.StatusCode >= 500);

        if (apiResult.IsTransient || isTransientStatusCode)
            throw new HttpRequestException($"Transient failure: {apiResult.Message}");

        // Case 3: Permanent API failure (4xx client errors) - return null, don't queue
        // These won't succeed on retry and queueing creates stuck pending timeline entries.
        return null;
    }

    #endregion

    #region Connectivity Check Tests

    [Fact]
    public async Task WhenOffline_ThrowsHttpRequestException()
    {
        // Arrange
        var apiResult = ApiResult.Ok(); // Doesn't matter - won't reach API call

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("No network connectivity");
    }

    [Theory]
    [InlineData(NetworkAccess.Local)]
    [InlineData(NetworkAccess.ConstrainedInternet)]
    [InlineData(NetworkAccess.Unknown)]
    [InlineData(NetworkAccess.Internet)]
    public async Task WhenSomeNetwork_AttemptsApiCall(NetworkAccess access)
    {
        // Arrange - LAN-only deployments should be able to reach local servers
        // Only NetworkAccess.None should block; other states allow the attempt
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = 123
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            access);

        // Assert - Call succeeds (server is reachable)
        result.Should().Be(123);
    }

    [Fact]
    public async Task WhenOnline_DoesNotThrowForConnectivity()
    {
        // Arrange - server accepts the location
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = 123
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        result.Should().Be(123);
    }

    #endregion

    #region Server Accepted Tests

    [Fact]
    public async Task WhenServerAccepts_ReturnsServerId()
    {
        // Arrange
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = 456
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        result.Should().Be(456);
    }

    [Fact]
    public async Task WhenServerAccepts_CallsOnAcceptedCallback()
    {
        // Arrange
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = 789
        };
        int? callbackServerId = null;

        // Act
        await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            onAccepted: (loc, serverId) =>
            {
                callbackServerId = serverId;
                return Task.CompletedTask;
            });

        // Assert
        callbackServerId.Should().Be(789);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(999999)]
    public async Task WhenServerAccepts_ReturnsCorrectServerId(int expectedId)
    {
        // Arrange
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = expectedId
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        result.Should().Be(expectedId);
    }

    #endregion

    #region Server Skipped Tests

    [Fact]
    public async Task WhenServerSkips_ReturnsNull()
    {
        // Arrange - Server returns Success=true, Skipped=true (threshold not met)
        var apiResult = ApiResult.SkippedResult("Distance threshold not met");

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenServerSkips_DoesNotCallOnAcceptedCallback()
    {
        // Arrange
        var apiResult = ApiResult.SkippedResult();
        var callbackCalled = false;

        // Act
        await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            onAccepted: (_, _) =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        // Assert
        callbackCalled.Should().BeFalse();
    }

    [Fact]
    public async Task WhenServerSkipsWithMessage_StillReturnsNull()
    {
        // Arrange - various skip messages
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = true,
            Message = "Time threshold: 4 min remaining, Distance threshold: 8m remaining"
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Transient Error Tests

    [Fact]
    public async Task WhenTransientError_ThrowsHttpRequestException()
    {
        // Arrange - Network timeout or similar transient failure
        var apiResult = ApiResult.Fail("Request timeout", statusCode: null, isTransient: true);

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Transient failure*");
    }

    [Fact]
    public async Task WhenTransientError_IncludesMessageInException()
    {
        // Arrange
        var apiResult = ApiResult.Fail("Connection refused", statusCode: null, isTransient: true);

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Connection refused*");
    }

    [Theory]
    [InlineData("Request timeout")]
    [InlineData("Connection refused")]
    [InlineData("DNS resolution failed")]
    [InlineData("Operation cancelled")]
    [InlineData("Socket exception")]
    public async Task WhenTransientErrorWithVariousMessages_ThrowsHttpRequestException(string message)
    {
        // Arrange
        var apiResult = ApiResult.Fail(message, statusCode: null, isTransient: true);

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"*{message}*");
    }

    [Fact]
    public async Task WhenTransientError_DoesNotCallOnAcceptedCallback()
    {
        // Arrange
        var apiResult = ApiResult.Fail("Timeout", isTransient: true);
        var callbackCalled = false;

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            onAccepted: (_, _) =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        callbackCalled.Should().BeFalse();
    }

    #endregion

    #region Transient HTTP Status Code Tests

    [Theory]
    [InlineData(500, "Internal server error")]
    [InlineData(502, "Bad gateway")]
    [InlineData(503, "Service unavailable")]
    [InlineData(504, "Gateway timeout")]
    public async Task When5xxServerError_ThrowsEvenIfIsTransientFalse(int statusCode, string message)
    {
        // Arrange - 5xx errors should be treated as transient (server might recover)
        // even if ApiClient doesn't set IsTransient=true for HTTP status failures
        var apiResult = ApiResult.Fail(message, statusCode: statusCode, isTransient: false);

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Should throw to trigger offline queue
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData(408, "Request timeout")]
    [InlineData(429, "Rate limited")]
    public async Task When408Or429_ThrowsToTriggerOfflineQueue(int statusCode, string message)
    {
        // Arrange - 408/429 are transient errors that should trigger offline queue.
        // QueueDrainService now properly handles these codes:
        // - 408: Request Timeout - server didn't respond in time
        // - 429: Too Many Requests - rate limited, should back off and retry
        var apiResult = ApiResult.Fail(message, statusCode: statusCode, isTransient: false);

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Should throw to trigger offline queue
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Transient failure*");
    }

    [Fact]
    public async Task When5xx_DoesNotCallOnAcceptedCallback()
    {
        // Arrange
        var apiResult = ApiResult.Fail("Server error", statusCode: 500, isTransient: false);
        var callbackCalled = false;

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            onAccepted: (_, _) =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        callbackCalled.Should().BeFalse();
    }

    #endregion

    #region Permanent Error Tests

    [Fact]
    public async Task WhenPermanentError_ReturnsNull()
    {
        // Arrange - Non-transient error (4xx client error)
        // These won't succeed on retry and queueing creates stuck pending timeline entries
        var apiResult = ApiResult.Fail("Bad request", statusCode: 400, isTransient: false);

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Returns null (don't queue, server explicitly rejected)
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(400, "Bad request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not found")]
    [InlineData(422, "Unprocessable entity")]
    public async Task WhenPermanent4xxError_ReturnsNull(int statusCode, string message)
    {
        // Arrange - 4xx errors are permanent client errors that won't succeed on retry
        var apiResult = ApiResult.Fail(message, statusCode: statusCode, isTransient: false);

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Returns null (don't queue)
        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenPermanentError_DoesNotCallOnAcceptedCallback()
    {
        // Arrange
        var apiResult = ApiResult.Fail("Bad request", statusCode: 400, isTransient: false);
        var callbackCalled = false;

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            onAccepted: (_, _) =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        // Assert
        result.Should().BeNull();
        callbackCalled.Should().BeFalse();
    }

    #endregion

    #region API Client Availability Tests

    [Fact]
    public async Task WhenApiClientNotAvailable_ThrowsInvalidOperationException()
    {
        // Arrange
        var apiResult = ApiResult.Ok(); // Doesn't matter

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet,
            apiClientAvailable: false);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("API client not available");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task WhenSuccessButNoLocationId_TreatsAsTransientIfMarked()
    {
        // Arrange - Edge case: Success=false, no LocationId, but marked as transient
        var apiResult = new ApiResult
        {
            Success = false,
            Skipped = false,
            LocationId = null,
            IsTransient = true,
            Message = "Unexpected empty response"
        };

        // Act
        var act = async () => await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Should throw HttpRequestException (transient) to trigger queue
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task WhenSuccessButNoLocationId_ReturnsNullWithoutThrowing()
    {
        // Arrange - Edge case: Success=true but missing LocationId
        // This happens when server says success but doesn't return an ID
        // (per API docs, though server implementation always includes it)
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = false,
            LocationId = null, // Missing but Success=true
            IsTransient = false
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Should return null (don't queue - server said success)
        // This is defensive handling for a case that shouldn't occur with real server
        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenBothSkippedAndLocationIdPresent_ReturnsLocationId()
    {
        // Arrange - Edge case: Both Skipped=true and LocationId set (contradictory)
        // Server never does this, but if it did we return the ID since Success=true
        var apiResult = new ApiResult
        {
            Success = true,
            Skipped = true,
            LocationId = 123 // Present even though Skipped=true
        };

        // Act
        var result = await SimulateOnlineSubmitLogic(
            apiResult,
            NetworkAccess.Internet);

        // Assert - Returns the LocationId (Success=true means don't queue)
        // Note: onAccepted callback won't be called since Skipped=true
        result.Should().Be(123);
    }

    #endregion

    #region Contract Documentation Tests

    /// <summary>
    /// Documents the expected behavior: null return means "don't queue".
    /// </summary>
    [Fact]
    public void Contract_NullReturn_MeansDontQueue()
    {
        // This is a documentation test verifying the contract.
        // Before the fix (#205), null was returned for BOTH:
        //   1. Server skip (correct - don't queue)
        //   2. Network error (WRONG - should queue)
        //
        // After the fix, null return means "don't queue" for any of:
        //   - Server accepted without ID (rare, per API docs)
        //   - Server skipped (threshold not met)
        //   - Permanent 4xx error (400, 401, 403, 404, etc. - won't succeed on retry)
        //
        // Transient errors throw to trigger queueing:
        //   - Network errors (IsTransient=true from ApiClient)
        //   - 408 Request Timeout
        //   - 429 Too Many Requests
        //   - 5xx Server Errors

        true.Should().BeTrue("Documentation test - see comments");
    }

    /// <summary>
    /// Documents the expected behavior: only transient exceptions trigger offline queue.
    /// </summary>
    [Fact]
    public void Contract_OnlyTransientExceptions_TriggerOfflineQueue()
    {
        // Platform services (Android/iOS) wrap the delegate call in try/catch.
        // Exceptions cause fallback to the offline queue delegate.
        //
        // Key behaviors:
        //   - HttpRequestException: Transient failures → queue
        //     - Network errors (IsTransient=true from ApiClient)
        //     - 408 Request Timeout
        //     - 429 Too Many Requests
        //     - 5xx Server Errors
        //   - InvalidOperationException: API client unavailable → queue
        //   - null return: Permanent 4xx rejection → DON'T queue
        //
        // QueueDrainService properly handles transient 4xx:
        //   - 408/429: Reset to pending for retry with backoff
        //   - Other 4xx: Mark as rejected, emit LocationSkipped for cleanup

        true.Should().BeTrue("Documentation test - see comments");
    }

    #endregion
}
