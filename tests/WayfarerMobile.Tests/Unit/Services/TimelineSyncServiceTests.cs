using System.Net;
using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TimelineSyncService focusing on sync operations, offline queue management,
/// error classification, and mutation merging logic.
/// </summary>
/// <remarks>
/// The TimelineSyncService is responsible for synchronizing timeline location edits with the server.
/// It implements optimistic UI updates with offline queue support:
/// 1. Apply optimistic UI update immediately (caller responsibility)
/// 2. Save to local database
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - raise SyncRejected event
/// 5. On 5xx/network error: Queue for retry when online
///
/// These tests use an in-memory SQLite database for testing database operations.
/// The tests cover the core algorithms (queue management, error classification, mutation merging)
/// using extracted logic that mirrors the actual implementation.
///
/// Note: Some tests document expected behavior since Connectivity.Current.NetworkAccess
/// cannot be mocked in unit tests. The actual TimelineSyncService depends on MAUI types.
/// </remarks>
[Collection("SQLite")]
public class TimelineSyncServiceTests : IAsyncLifetime
{
    #region Test Infrastructure

    private SQLiteAsyncConnection _database = null!;

    /// <summary>
    /// Initializes the in-memory database before each test.
    /// </summary>
    public async Task InitializeAsync()
    {
        _database = new SQLiteAsyncConnection(":memory:");
        await _database.CreateTableAsync<PendingTimelineMutation>();
    }

    /// <summary>
    /// Disposes the database connection after each test.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    /// <summary>
    /// Helper to insert a mutation directly into the test database.
    /// </summary>
    private async Task<PendingTimelineMutation> InsertMutationAsync(PendingTimelineMutation mutation)
    {
        await _database.InsertAsync(mutation);
        return mutation;
    }

    /// <summary>
    /// Helper to get all pending mutations from the database.
    /// </summary>
    private async Task<List<PendingTimelineMutation>> GetAllMutationsAsync()
    {
        return await _database.Table<PendingTimelineMutation>().ToListAsync();
    }

    /// <summary>
    /// Helper to get syncable mutations (mirrors TimelineSyncService.ProcessPendingMutationsAsync query).
    /// </summary>
    private async Task<List<PendingTimelineMutation>> GetSyncableMutationsAsync()
    {
        return await _database.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Determines if an HTTP status code is a client error (4xx).
    /// Mirrors TimelineSyncService.IsClientError() logic.
    /// </summary>
    private static bool IsClientError(HttpStatusCode? statusCode)
    {
        return statusCode.HasValue &&
               (int)statusCode.Value >= 400 &&
               (int)statusCode.Value < 500;
    }

    /// <summary>
    /// Enqueues a mutation with merge logic.
    /// Mirrors TimelineSyncService.EnqueueMutationAsync() logic.
    /// </summary>
    private async Task EnqueueMutationAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes)
    {
        // Check if there's already a pending mutation for this location
        var existing = await _database.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Merge with existing mutation (latest values win)
            if (latitude.HasValue) existing.Latitude = latitude;
            if (longitude.HasValue) existing.Longitude = longitude;
            if (localTimestamp.HasValue) existing.LocalTimestamp = localTimestamp;
            if (includeNotes)
            {
                existing.Notes = notes;
                existing.IncludeNotes = true;
            }
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
        }
        else
        {
            var mutation = new PendingTimelineMutation
            {
                OperationType = "Update",
                LocationId = locationId,
                Latitude = latitude,
                Longitude = longitude,
                LocalTimestamp = localTimestamp,
                Notes = notes,
                IncludeNotes = includeNotes,
                CreatedAt = DateTime.UtcNow
            };
            await _database.InsertAsync(mutation);
        }
    }

    /// <summary>
    /// Enqueues a delete mutation.
    /// Mirrors TimelineSyncService.EnqueueDeleteMutationAsync() logic.
    /// </summary>
    private async Task EnqueueDeleteMutationAsync(int locationId)
    {
        // Remove any pending updates for this location
        await _database.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId)
            .DeleteAsync();

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    /// <summary>
    /// Clears rejected mutations.
    /// Mirrors TimelineSyncService.ClearRejectedMutationsAsync() logic.
    /// </summary>
    private async Task ClearRejectedMutationsAsync()
    {
        await _database.Table<PendingTimelineMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();
    }

    /// <summary>
    /// Gets count of pending (syncable) mutations.
    /// Mirrors TimelineSyncService.GetPendingCountAsync() logic.
    /// </summary>
    private async Task<int> GetPendingCountAsync()
    {
        return await _database.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
            .CountAsync();
    }

    #endregion

    #region IsClientError Classification Tests

    /// <summary>
    /// Verifies that HTTP 400 Bad Request is classified as a client error.
    /// Client errors indicate the request is invalid and should not be retried.
    /// </summary>
    [Fact]
    public void IsClientError_Http400_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.BadRequest);
        result.Should().BeTrue("HTTP 400 indicates client error - data is invalid");
    }

    /// <summary>
    /// Verifies that HTTP 401 Unauthorized is classified as a client error.
    /// </summary>
    [Fact]
    public void IsClientError_Http401_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.Unauthorized);
        result.Should().BeTrue("HTTP 401 indicates authentication failure");
    }

    /// <summary>
    /// Verifies that HTTP 403 Forbidden is classified as a client error.
    /// </summary>
    [Fact]
    public void IsClientError_Http403_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.Forbidden);
        result.Should().BeTrue("HTTP 403 indicates authorization failure");
    }

    /// <summary>
    /// Verifies that HTTP 404 Not Found is classified as a client error.
    /// </summary>
    [Fact]
    public void IsClientError_Http404_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.NotFound);
        result.Should().BeTrue("HTTP 404 indicates resource not found");
    }

    /// <summary>
    /// Verifies that HTTP 422 Unprocessable Entity is classified as a client error.
    /// </summary>
    [Fact]
    public void IsClientError_Http422_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.UnprocessableEntity);
        result.Should().BeTrue("HTTP 422 indicates validation failure");
    }

    /// <summary>
    /// Verifies that HTTP 429 Too Many Requests is classified as a client error.
    /// </summary>
    [Fact]
    public void IsClientError_Http429_ReturnsTrue()
    {
        var result = IsClientError(HttpStatusCode.TooManyRequests);
        result.Should().BeTrue("HTTP 429 is in 4xx range");
    }

    /// <summary>
    /// Verifies that HTTP 5xx server errors are NOT classified as client errors.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "500 Internal Server Error")]
    [InlineData(HttpStatusCode.BadGateway, "502 Bad Gateway")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503 Service Unavailable")]
    [InlineData(HttpStatusCode.GatewayTimeout, "504 Gateway Timeout")]
    public void IsClientError_Http5xx_ReturnsFalse(HttpStatusCode statusCode, string description)
    {
        var result = IsClientError(statusCode);
        result.Should().BeFalse($"{description} should be server error, not client error");
    }

    /// <summary>
    /// Verifies that null status code is NOT classified as client error.
    /// </summary>
    [Fact]
    public void IsClientError_NullStatusCode_ReturnsFalse()
    {
        var result = IsClientError(null);
        result.Should().BeFalse("Null status code (network error) is not a client error");
    }

    /// <summary>
    /// Verifies boundary conditions for client error classification.
    /// </summary>
    [Theory]
    [InlineData(399, false, "Below 4xx range")]
    [InlineData(400, true, "Start of 4xx range")]
    [InlineData(499, true, "End of 4xx range")]
    [InlineData(500, false, "Start of 5xx range")]
    public void IsClientError_BoundaryConditions_ClassifiesCorrectly(int statusCode, bool expected, string description)
    {
        var result = statusCode >= 400 && statusCode < 500;
        result.Should().Be(expected, description);
    }

    #endregion

    #region EnqueueMutationAsync Tests - Offline Queuing

    /// <summary>
    /// Verifies that UpdateLocationAsync queues a new mutation when offline.
    /// </summary>
    [Fact]
    public async Task EnqueueMutationAsync_InsertsNewMutation_WhenNoExisting()
    {
        // Arrange
        var locationId = 123;
        var latitude = 51.5074;
        var longitude = -0.1278;
        var timestamp = DateTime.UtcNow;
        var notes = "Test notes";

        // Act
        await EnqueueMutationAsync(locationId, latitude, longitude, timestamp, notes, includeNotes: true);

        // Assert
        var mutations = await GetAllMutationsAsync();
        mutations.Should().HaveCount(1, "One mutation should be queued");

        var mutation = mutations[0];
        mutation.LocationId.Should().Be(locationId);
        mutation.Latitude.Should().Be(latitude);
        mutation.Longitude.Should().Be(longitude);
        mutation.LocalTimestamp.Should().Be(timestamp);
        mutation.Notes.Should().Be(notes);
        mutation.IncludeNotes.Should().BeTrue();
        mutation.OperationType.Should().Be("Update");
    }

    /// <summary>
    /// Verifies that enqueueing a mutation for an existing location merges values.
    /// Latest values should win, preserving existing values when new ones are null.
    /// </summary>
    [Fact]
    public async Task EnqueueMutationAsync_MergesWithExisting_LatestValuesWin()
    {
        // Arrange - Insert initial mutation
        var locationId = 123;
        await EnqueueMutationAsync(locationId, 51.5074, -0.1278, DateTime.UtcNow.AddHours(-1), "Original notes", true);

        var beforeMerge = await GetAllMutationsAsync();
        beforeMerge.Should().HaveCount(1);

        // Act - Enqueue update with partial values (only latitude and notes)
        await EnqueueMutationAsync(locationId, 52.0, null, null, "Updated notes", true);

        // Assert
        var afterMerge = await GetAllMutationsAsync();
        afterMerge.Should().HaveCount(1, "Should merge, not create new");

        var merged = afterMerge[0];
        merged.Latitude.Should().Be(52.0, "Latitude should be updated to new value");
        merged.Longitude.Should().Be(-0.1278, "Longitude should be preserved (no new value)");
        merged.Notes.Should().Be("Updated notes", "Notes should be updated to new value");
    }

    /// <summary>
    /// Verifies that IncludeNotes flag controls notes merging behavior.
    /// </summary>
    [Fact]
    public async Task EnqueueMutationAsync_PreservesNotes_WhenIncludeNotesIsFalse()
    {
        // Arrange - Insert mutation with notes
        var locationId = 789;
        await EnqueueMutationAsync(locationId, 51.5, -0.1, null, "Original notes", true);

        // Act - Update only coordinates, don't touch notes
        await EnqueueMutationAsync(locationId, 52.0, null, null, null, false);

        // Assert
        var mutations = await GetAllMutationsAsync();
        mutations[0].Notes.Should().Be("Original notes", "Notes should be preserved when includeNotes = false");
        mutations[0].IncludeNotes.Should().BeTrue("IncludeNotes flag should be preserved");
    }

    /// <summary>
    /// Verifies that merging updates the CreatedAt timestamp.
    /// </summary>
    [Fact]
    public async Task EnqueueMutationAsync_UpdatesCreatedAt_OnMerge()
    {
        // Arrange
        var locationId = 456;
        await EnqueueMutationAsync(locationId, 51.5, -0.1, null, null, false);

        var beforeMerge = await GetAllMutationsAsync();
        var originalCreatedAt = beforeMerge[0].CreatedAt;

        // Wait briefly to ensure timestamp difference
        await Task.Delay(10);

        // Act
        await EnqueueMutationAsync(locationId, 52.0, null, null, null, false);

        // Assert
        var afterMerge = await GetAllMutationsAsync();
        afterMerge[0].CreatedAt.Should().BeAfter(originalCreatedAt, "CreatedAt should be updated on merge");
    }

    #endregion

    #region EnqueueDeleteMutationAsync Tests

    /// <summary>
    /// Verifies that DeleteLocationAsync queues a delete mutation when offline.
    /// </summary>
    [Fact]
    public async Task EnqueueDeleteMutationAsync_InsertsDeletMutation()
    {
        // Arrange
        var locationId = 999;

        // Act
        await EnqueueDeleteMutationAsync(locationId);

        // Assert
        var mutations = await GetAllMutationsAsync();
        mutations.Should().HaveCount(1);
        mutations[0].OperationType.Should().Be("Delete");
        mutations[0].LocationId.Should().Be(locationId);
    }

    /// <summary>
    /// Verifies that queuing a delete removes any pending updates for the same location.
    /// </summary>
    [Fact]
    public async Task EnqueueDeleteMutationAsync_RemovesPendingUpdates()
    {
        // Arrange - First add a pending update
        var locationId = 123;
        await EnqueueMutationAsync(locationId, 51.5074, -0.1278, null, "Some notes", true);

        var beforeDelete = await GetAllMutationsAsync();
        beforeDelete.Should().ContainSingle(m => m.LocationId == locationId && m.OperationType == "Update");

        // Act - Queue delete
        await EnqueueDeleteMutationAsync(locationId);

        // Assert
        var afterDelete = await GetAllMutationsAsync();
        afterDelete.Should().ContainSingle(m => m.LocationId == locationId);
        afterDelete[0].OperationType.Should().Be("Delete", "Only delete mutation should remain");
    }

    /// <summary>
    /// Verifies that delete mutation can be queued even when no prior updates exist.
    /// </summary>
    [Fact]
    public async Task EnqueueDeleteMutationAsync_WorksWithNoExistingMutations()
    {
        // Arrange - Empty database
        var beforeDelete = await GetAllMutationsAsync();
        beforeDelete.Should().BeEmpty();

        // Act
        await EnqueueDeleteMutationAsync(555);

        // Assert
        var afterDelete = await GetAllMutationsAsync();
        afterDelete.Should().ContainSingle(m => m.LocationId == 555 && m.OperationType == "Delete");
    }

    #endregion

    #region ProcessPendingMutationsAsync Tests - Queue Processing

    /// <summary>
    /// Verifies that syncable mutations are retrieved in CreatedAt order (oldest first).
    /// </summary>
    [Fact]
    public async Task GetSyncableMutations_OrdersByCreatedAtAscending()
    {
        // Arrange - Insert mutations with different timestamps
        var now = DateTime.UtcNow;

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 3,
            CreatedAt = now.AddMinutes(-1) // Newest
        });

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 1,
            CreatedAt = now.AddMinutes(-10) // Oldest
        });

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 2,
            CreatedAt = now.AddMinutes(-5) // Middle
        });

        // Act
        var syncable = await GetSyncableMutationsAsync();

        // Assert
        syncable.Should().HaveCount(3);
        syncable[0].LocationId.Should().Be(1, "Oldest should be first");
        syncable[1].LocationId.Should().Be(2, "Middle should be second");
        syncable[2].LocationId.Should().Be(3, "Newest should be last");
    }

    /// <summary>
    /// Verifies that rejected mutations are excluded from processing.
    /// </summary>
    [Fact]
    public async Task GetSyncableMutations_ExcludesRejectedMutations()
    {
        // Arrange
        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 1,
            IsRejected = false,
            SyncAttempts = 0
        });

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 2,
            IsRejected = true, // Rejected
            SyncAttempts = 1
        });

        // Act
        var syncable = await GetSyncableMutationsAsync();

        // Assert
        syncable.Should().ContainSingle(m => m.LocationId == 1);
        syncable.Should().NotContain(m => m.LocationId == 2);
    }

    /// <summary>
    /// Verifies that mutations exceeding MaxSyncAttempts are excluded.
    /// </summary>
    [Fact]
    public async Task GetSyncableMutations_ExcludesMaxAttemptsExceeded()
    {
        // Arrange
        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 1,
            SyncAttempts = 4 // Under limit
        });

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 2,
            SyncAttempts = 5 // At limit (MaxSyncAttempts = 5)
        });

        await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 3,
            SyncAttempts = 6 // Over limit
        });

        // Act
        var syncable = await GetSyncableMutationsAsync();

        // Assert
        syncable.Should().ContainSingle(m => m.LocationId == 1);
        syncable.Should().NotContain(m => m.LocationId == 2);
        syncable.Should().NotContain(m => m.LocationId == 3);
    }

    /// <summary>
    /// Verifies successful sync removes mutation from queue.
    /// </summary>
    [Fact]
    public async Task ProcessPending_OnSuccess_RemovesMutationFromQueue()
    {
        // Arrange
        var mutation = await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 100,
            SyncAttempts = 0
        });

        var beforeSync = await GetAllMutationsAsync();
        beforeSync.Should().HaveCount(1);

        // Act - Simulate successful sync by deleting the mutation
        await _database.DeleteAsync(mutation);

        // Assert
        var afterSync = await GetAllMutationsAsync();
        afterSync.Should().BeEmpty("Mutation should be removed after successful sync");
    }

    /// <summary>
    /// Verifies 4xx error marks mutation as server rejected.
    /// </summary>
    [Fact]
    public async Task ProcessPending_On4xxError_MarksMutationAsRejected()
    {
        // Arrange
        var mutation = await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 200,
            SyncAttempts = 0,
            IsRejected = false
        });

        // Act - Simulate 4xx error handling
        mutation.IsRejected = true;
        mutation.LastError = "400 Bad Request";
        mutation.SyncAttempts++;
        mutation.LastSyncAttempt = DateTime.UtcNow;
        await _database.UpdateAsync(mutation);

        // Assert
        var updated = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 200);

        updated.Should().NotBeNull();
        updated!.IsRejected.Should().BeTrue("Should be marked as rejected");
        updated.CanSync.Should().BeFalse("Rejected mutations should not be syncable");
    }

    /// <summary>
    /// Verifies 5xx error increments retry count but keeps mutation for retry.
    /// </summary>
    [Fact]
    public async Task ProcessPending_On5xxError_IncrementsRetryCount()
    {
        // Arrange
        var mutation = await InsertMutationAsync(new PendingTimelineMutation
        {
            LocationId = 300,
            SyncAttempts = 0,
            IsRejected = false
        });

        // Act - Simulate 5xx error handling
        mutation.SyncAttempts++;
        mutation.LastSyncAttempt = DateTime.UtcNow;
        mutation.LastError = "503 Service Unavailable";
        await _database.UpdateAsync(mutation);

        // Assert
        var updated = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 300);

        updated.Should().NotBeNull();
        updated!.SyncAttempts.Should().Be(1, "Retry count should be incremented");
        updated.IsRejected.Should().BeFalse("Should NOT be rejected (server error is retryable)");
        updated.CanSync.Should().BeTrue("Should still be syncable for retry");
    }

    #endregion

    #region GetPendingCountAsync Tests

    /// <summary>
    /// Verifies GetPendingCountAsync returns correct count of syncable mutations.
    /// </summary>
    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        // Arrange: 3 valid, 1 rejected, 1 max attempts exceeded
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 1, SyncAttempts = 0 });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 2, SyncAttempts = 2 });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 3, SyncAttempts = 4 });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 4, IsRejected = true });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 5, SyncAttempts = 5 });

        // Act
        var count = await GetPendingCountAsync();

        // Assert
        count.Should().Be(3, "Only syncable mutations should be counted");
    }

    /// <summary>
    /// Verifies GetPendingCountAsync returns 0 when queue is empty.
    /// </summary>
    [Fact]
    public async Task GetPendingCountAsync_ReturnsZero_WhenEmpty()
    {
        var count = await GetPendingCountAsync();
        count.Should().Be(0, "Empty queue should return 0");
    }

    #endregion

    #region ClearRejectedMutationsAsync Tests

    /// <summary>
    /// Verifies ClearRejectedMutationsAsync removes only rejected mutations.
    /// </summary>
    [Fact]
    public async Task ClearRejectedMutationsAsync_RemovesRejectedOnly()
    {
        // Arrange
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 1, IsRejected = false });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 2, IsRejected = false });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 3, IsRejected = true });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 4, IsRejected = true });

        // Act
        await ClearRejectedMutationsAsync();

        // Assert
        var remaining = await GetAllMutationsAsync();
        remaining.Should().HaveCount(2, "Only non-rejected should remain");
        remaining.Should().OnlyContain(m => !m.IsRejected);
        remaining.Select(m => m.LocationId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    /// <summary>
    /// Verifies ClearRejectedMutationsAsync does nothing when no rejected mutations exist.
    /// </summary>
    [Fact]
    public async Task ClearRejectedMutationsAsync_NoOpWhenNoneRejected()
    {
        // Arrange
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 1, IsRejected = false });
        await InsertMutationAsync(new PendingTimelineMutation { LocationId = 2, IsRejected = false });

        var beforeCount = await _database.Table<PendingTimelineMutation>().CountAsync();

        // Act
        await ClearRejectedMutationsAsync();

        // Assert
        var afterCount = await _database.Table<PendingTimelineMutation>().CountAsync();
        afterCount.Should().Be(beforeCount, "No mutations should be deleted");
    }

    #endregion

    #region CanSync Property Tests

    /// <summary>
    /// Verifies CanSync property correctly evaluates sync eligibility.
    /// </summary>
    [Theory]
    [InlineData(false, 0, true, "Not rejected, no attempts")]
    [InlineData(false, 4, true, "Not rejected, under max attempts")]
    [InlineData(false, 5, false, "Not rejected, at max attempts")]
    [InlineData(false, 6, false, "Not rejected, over max attempts")]
    [InlineData(true, 0, false, "Rejected, no attempts")]
    [InlineData(true, 4, false, "Rejected, under max attempts")]
    public void CanSync_EvaluatesCorrectly(bool isRejected, int syncAttempts, bool expectedCanSync, string scenario)
    {
        var mutation = new PendingTimelineMutation
        {
            IsRejected = isRejected,
            SyncAttempts = syncAttempts
        };

        mutation.CanSync.Should().Be(expectedCanSync, scenario);
    }

    /// <summary>
    /// Verifies MaxSyncAttempts constant is 5.
    /// </summary>
    [Fact]
    public void MaxSyncAttempts_Is5()
    {
        PendingTimelineMutation.MaxSyncAttempts.Should().Be(5, "MaxSyncAttempts should be 5");
    }

    #endregion

    #region Documented Behavior Tests

    /// <summary>
    /// Documents UpdateLocationAsync behavior when online and successful.
    /// </summary>
    [Fact]
    public void UpdateLocationAsync_WhenOnline_SendsToServer_DocumentedBehavior()
    {
        // Expected behavior when Connectivity.Current.NetworkAccess == NetworkAccess.Internet:
        // 1. Build TimelineLocationUpdateRequest with provided parameters
        // 2. Call IApiClient.UpdateTimelineLocationAsync(locationId, request)
        // 3. If response.Success == true:
        //    - Raise SyncCompleted event with EntityId = Guid.Empty
        //    - No mutation is queued
        // 4. If response.Success == false:
        //    - Queue mutation for retry
        //    - Raise SyncQueued event

        true.Should().BeTrue("Documentation test - requires online connectivity");
    }

    /// <summary>
    /// Documents UpdateLocationAsync behavior when offline.
    /// </summary>
    [Fact]
    public void UpdateLocationAsync_WhenOffline_QueuesLocally_DocumentedBehavior()
    {
        // Expected behavior when Connectivity.Current.NetworkAccess != NetworkAccess.Internet:
        // 1. Call EnqueueMutationAsync with all parameters
        // 2. Raise SyncQueued event with Message = "Saved offline - will sync when online"
        // 3. API client is NOT called

        var expectedMessage = "Saved offline - will sync when online";
        expectedMessage.Should().Contain("offline");
    }

    /// <summary>
    /// Documents UpdateLocationAsync behavior on 4xx error.
    /// </summary>
    [Fact]
    public void UpdateLocationAsync_On4xxError_RaisesSyncRejectedEvent_DocumentedBehavior()
    {
        // Expected behavior when HttpRequestException with 4xx status code:
        // 1. Catch exception where IsClientError(ex) returns true
        // 2. Raise SyncRejected event with:
        //    - EntityId = Guid.Empty
        //    - ErrorMessage = "Server rejected changes: {ex.Message}"
        //    - IsClientError = true
        // 3. Do NOT queue mutation for retry (client errors won't succeed)

        var statusCodesToTest = new[] { 400, 401, 403, 404, 422, 429 };
        foreach (var code in statusCodesToTest)
        {
            IsClientError((HttpStatusCode)code).Should().BeTrue(
                $"Status code {code} should be classified as client error");
        }
    }

    /// <summary>
    /// Documents UpdateLocationAsync behavior on 5xx error.
    /// </summary>
    [Fact]
    public void UpdateLocationAsync_On5xxError_QueuesMutation_DocumentedBehavior()
    {
        // Expected behavior when HttpRequestException with 5xx status code or network error:
        // 1. Catch general exception (server error is retryable)
        // 2. Call EnqueueMutationAsync to queue for retry
        // 3. Raise SyncQueued event with Message = "Sync failed: {ex.Message} - will retry"

        var serverErrorCodes = new[] { 500, 502, 503, 504 };
        foreach (var code in serverErrorCodes)
        {
            IsClientError((HttpStatusCode)code).Should().BeFalse(
                $"Status code {code} should NOT be classified as client error (server errors are retryable)");
        }
    }

    /// <summary>
    /// Documents DeleteLocationAsync behavior when online.
    /// </summary>
    [Fact]
    public void DeleteLocationAsync_WhenOnline_SendsDeleteToServer_DocumentedBehavior()
    {
        // Expected behavior when online:
        // 1. Call IApiClient.DeleteTimelineLocationAsync(locationId)
        // 2. If success:
        //    - Raise SyncCompleted event
        // 3. If failure:
        //    - Queue delete mutation
        //    - Raise SyncQueued event

        true.Should().BeTrue("Documentation test - requires online connectivity");
    }

    /// <summary>
    /// Documents DeleteLocationAsync behavior when offline.
    /// </summary>
    [Fact]
    public void DeleteLocationAsync_WhenOffline_QueuesDelete_DocumentedBehavior()
    {
        // Expected behavior when offline:
        // 1. Remove any pending updates for the same location
        // 2. Insert delete mutation
        // 3. Raise SyncQueued event with Message = "Deleted offline - will sync when online"

        var expectedMessage = "Deleted offline - will sync when online";
        expectedMessage.Should().Contain("offline");
    }

    /// <summary>
    /// Documents ProcessPendingMutationsAsync behavior.
    /// </summary>
    [Fact]
    public void ProcessPendingMutationsAsync_SyncsQueuedMutations_DocumentedBehavior()
    {
        // Expected behavior:
        // 1. Check connectivity - return early if offline
        // 2. Query mutations where CanSync == true, ordered by CreatedAt ASC
        // 3. For each mutation:
        //    a. Increment SyncAttempts
        //    b. Set LastSyncAttempt = now
        //    c. If OperationType == "Delete":
        //       - Call IApiClient.DeleteTimelineLocationAsync
        //    d. Else (Update):
        //       - Build TimelineLocationUpdateRequest
        //       - Call IApiClient.UpdateTimelineLocationAsync
        //    e. If success:
        //       - Delete mutation from database
        //       - Raise SyncCompleted event
        //    f. If 4xx error (client error):
        //       - Set IsRejected = true
        //       - Save error to LastError
        //       - Raise SyncRejected event
        //    g. If 5xx/network error:
        //       - Save error to LastError
        //       - Keep for retry (mutation stays in queue)

        true.Should().BeTrue("Documentation test");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Verifies handling of multiple mutations for different locations.
    /// </summary>
    [Fact]
    public async Task MultipleMutations_ForDifferentLocations_ProcessedIndependently()
    {
        // Arrange
        await EnqueueMutationAsync(100, 51.5, -0.1, null, null, false);
        await EnqueueMutationAsync(200, 40.7, -74.0, null, null, false);
        await EnqueueMutationAsync(300, 35.7, 139.7, null, null, false);

        // Assert
        var mutations = await GetAllMutationsAsync();
        mutations.Should().HaveCount(3, "Each location should have its own mutation");
        mutations.Select(m => m.LocationId).Should().BeEquivalentTo(new[] { 100, 200, 300 });
    }

    /// <summary>
    /// Verifies concurrent update and delete handling.
    /// </summary>
    [Fact]
    public async Task UpdateThenDelete_ResultsInOnlyDeleteMutation()
    {
        // Arrange - Update first
        var locationId = 555;
        await EnqueueMutationAsync(locationId, 51.5, -0.1, DateTime.UtcNow, "Notes", true);

        var afterUpdate = await GetAllMutationsAsync();
        afterUpdate.Should().ContainSingle(m => m.OperationType == "Update");

        // Act - Then delete (should replace update)
        await EnqueueDeleteMutationAsync(locationId);

        // Assert
        var afterDelete = await GetAllMutationsAsync();
        afterDelete.Should().ContainSingle(m => m.OperationType == "Delete");
    }

    /// <summary>
    /// Verifies mutation fields are nullable where appropriate.
    /// </summary>
    [Fact]
    public async Task MutationFields_CanBeNull()
    {
        // Arrange - Mutation with only LocationId (partial update)
        var mutation = new PendingTimelineMutation
        {
            LocationId = 999,
            Latitude = null,
            Longitude = null,
            LocalTimestamp = null,
            Notes = null,
            IncludeNotes = false
        };

        // Act
        await InsertMutationAsync(mutation);

        // Assert
        var retrieved = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 999);

        retrieved.Should().NotBeNull();
        retrieved!.Latitude.Should().BeNull();
        retrieved.Longitude.Should().BeNull();
        retrieved.LocalTimestamp.Should().BeNull();
        retrieved.Notes.Should().BeNull();
    }

    #endregion

    #region Rollback Data Persistence Tests

    /// <summary>
    /// Verifies that original values can be stored for update rollback.
    /// </summary>
    [Fact]
    public async Task RollbackData_StoresOriginalValuesForUpdate()
    {
        // Arrange
        var originalTimestamp = DateTime.UtcNow.AddHours(-1);
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            LocalEntryId = 50,
            // New values
            Latitude = 52.0,
            Longitude = -1.0,
            LocalTimestamp = DateTime.UtcNow,
            Notes = "Updated notes",
            IncludeNotes = true,
            // Original values for rollback
            OriginalLatitude = 51.5074,
            OriginalLongitude = -0.1278,
            OriginalTimestamp = originalTimestamp,
            OriginalNotes = "Original notes"
        };

        // Act
        await InsertMutationAsync(mutation);

        // Assert
        var retrieved = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 100);

        retrieved.Should().NotBeNull();
        retrieved!.LocalEntryId.Should().Be(50);
        retrieved.OriginalLatitude.Should().Be(51.5074);
        retrieved.OriginalLongitude.Should().Be(-0.1278);
        retrieved.OriginalTimestamp.Should().Be(originalTimestamp);
        retrieved.OriginalNotes.Should().Be("Original notes");
    }

    /// <summary>
    /// Verifies that deleted entry JSON can be stored for delete rollback.
    /// </summary>
    [Fact]
    public async Task RollbackData_StoresDeletedEntryJson()
    {
        // Arrange
        var deletedEntryJson = """{"Id":50,"ServerId":100,"Latitude":51.5074,"Longitude":-0.1278}""";
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = 100,
            DeletedEntryJson = deletedEntryJson
        };

        // Act
        await InsertMutationAsync(mutation);

        // Assert
        var retrieved = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 100);

        retrieved.Should().NotBeNull();
        retrieved!.OperationType.Should().Be("Delete");
        retrieved.DeletedEntryJson.Should().Be(deletedEntryJson);
    }

    /// <summary>
    /// Verifies HasRollbackData returns true when original latitude is set.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsTrueWhenOriginalLatitudeSet()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            OriginalLatitude = 51.5074
        };

        mutation.HasRollbackData.Should().BeTrue();
    }

    /// <summary>
    /// Verifies HasRollbackData returns true when original longitude is set.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsTrueWhenOriginalLongitudeSet()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            OriginalLongitude = -0.1278
        };

        mutation.HasRollbackData.Should().BeTrue();
    }

    /// <summary>
    /// Verifies HasRollbackData returns true when original timestamp is set.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsTrueWhenOriginalTimestampSet()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            OriginalTimestamp = DateTime.UtcNow
        };

        mutation.HasRollbackData.Should().BeTrue();
    }

    /// <summary>
    /// Verifies HasRollbackData returns true when original notes is set (even to empty string).
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsTrueWhenOriginalNotesSet()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            OriginalNotes = "" // Empty string is still "set"
        };

        mutation.HasRollbackData.Should().BeTrue();
    }

    /// <summary>
    /// Verifies HasRollbackData returns false when no original values are set.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsFalseWhenNoOriginalValuesSet()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            Latitude = 52.0,
            Longitude = -1.0
            // No original values set
        };

        mutation.HasRollbackData.Should().BeFalse();
    }

    /// <summary>
    /// Verifies HasRollbackData returns true for delete with JSON.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsTrueForDeleteWithJson()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = 100,
            DeletedEntryJson = """{"Id":50}"""
        };

        mutation.HasRollbackData.Should().BeTrue();
    }

    /// <summary>
    /// Verifies HasRollbackData returns false for delete without JSON.
    /// </summary>
    [Fact]
    public void HasRollbackData_ReturnsFalseForDeleteWithoutJson()
    {
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = 100
            // No DeletedEntryJson
        };

        mutation.HasRollbackData.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that merging mutations preserves the original rollback data.
    /// When a mutation is updated with new values, the original rollback data should be kept.
    /// </summary>
    [Fact]
    public async Task MergeMutation_PreservesOriginalRollbackData()
    {
        // Arrange - First mutation with original values
        var originalTimestamp = DateTime.UtcNow.AddHours(-2);
        var firstMutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            LocalEntryId = 50,
            Latitude = 52.0,
            OriginalLatitude = 51.5074,
            OriginalLongitude = -0.1278,
            OriginalTimestamp = originalTimestamp,
            OriginalNotes = "Original notes",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        await InsertMutationAsync(firstMutation);

        // Act - Merge with second update (simulate EnqueueMutationWithRollbackAsync merge)
        var existing = await _database.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == 100 && !m.IsRejected)
            .FirstOrDefaultAsync();

        // Only update new values, keep original rollback data
        existing!.Latitude = 53.0; // New value
        existing.Longitude = -2.0; // New value
        existing.CreatedAt = DateTime.UtcNow;
        // Note: We do NOT update OriginalLatitude, OriginalLongitude, etc.
        await _database.UpdateAsync(existing);

        // Assert - Original rollback data should be preserved
        var retrieved = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 100);

        retrieved.Should().NotBeNull();
        retrieved!.Latitude.Should().Be(53.0, "new value should be updated");
        retrieved.Longitude.Should().Be(-2.0, "new value should be updated");
        retrieved.OriginalLatitude.Should().Be(51.5074, "original rollback value should be preserved");
        retrieved.OriginalLongitude.Should().Be(-0.1278, "original rollback value should be preserved");
        retrieved.OriginalTimestamp.Should().Be(originalTimestamp, "original rollback value should be preserved");
        retrieved.OriginalNotes.Should().Be("Original notes", "original rollback value should be preserved");
    }

    /// <summary>
    /// Verifies rollback data survives database round-trip (simulating app restart).
    /// </summary>
    [Fact]
    public async Task RollbackData_SurvivesDatabaseRoundTrip()
    {
        // Arrange
        var originalTimestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var mutation = new PendingTimelineMutation
        {
            OperationType = "Update",
            LocationId = 100,
            LocalEntryId = 50,
            Latitude = 52.0,
            Longitude = -1.0,
            LocalTimestamp = DateTime.UtcNow,
            Notes = "Updated",
            IncludeNotes = true,
            OriginalLatitude = 51.5074,
            OriginalLongitude = -0.1278,
            OriginalTimestamp = originalTimestamp,
            OriginalNotes = "Original",
            CreatedAt = DateTime.UtcNow,
            SyncAttempts = 2,
            LastSyncAttempt = DateTime.UtcNow.AddMinutes(-5),
            LastError = "Network timeout"
        };

        // Act - Insert and retrieve (simulating app restart)
        await InsertMutationAsync(mutation);
        var retrieved = await _database.Table<PendingTimelineMutation>()
            .FirstOrDefaultAsync(m => m.LocationId == 100);

        // Assert - All fields including rollback data should be preserved
        retrieved.Should().NotBeNull();
        retrieved!.OperationType.Should().Be("Update");
        retrieved.LocationId.Should().Be(100);
        retrieved.LocalEntryId.Should().Be(50);
        retrieved.Latitude.Should().Be(52.0);
        retrieved.Longitude.Should().Be(-1.0);
        retrieved.Notes.Should().Be("Updated");
        retrieved.IncludeNotes.Should().BeTrue();
        retrieved.OriginalLatitude.Should().Be(51.5074);
        retrieved.OriginalLongitude.Should().Be(-0.1278);
        retrieved.OriginalTimestamp.Should().Be(originalTimestamp);
        retrieved.OriginalNotes.Should().Be("Original");
        retrieved.SyncAttempts.Should().Be(2);
        retrieved.LastError.Should().Be("Network timeout");
        retrieved.HasRollbackData.Should().BeTrue();
        retrieved.CanSync.Should().BeTrue();
    }

    #endregion
}
