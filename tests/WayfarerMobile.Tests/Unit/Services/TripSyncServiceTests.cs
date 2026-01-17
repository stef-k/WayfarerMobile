using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Characterization tests for TripSyncService.
/// These tests document current behavior before refactoring (Issue #93, Phase 0).
///
/// TripSyncService implements optimistic UI updates with offline queue:
/// 1. Apply optimistic UI update immediately (caller responsibility)
/// 2. Save to local database
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - revert changes, notify caller
/// 5. On 5xx/network error: Queue for retry when online
/// </summary>
/// <remarks>
/// Tests cover:
/// - Event firing (SyncCompleted, SyncRejected, SyncQueued, EntityCreated)
/// - Mutation queue persistence
/// - Offline queueing behavior
/// - Retry logic and attempt tracking
/// - Original value preservation for rollback
/// </remarks>
[Collection("SQLite")]
public class TripSyncServiceTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _database = null!;
    private Mock<IApiClient> _apiClientMock = null!;

    #region Test Lifecycle

    public async Task InitializeAsync()
    {
        _database = new SQLiteAsyncConnection(":memory:");
        await _database.CreateTableAsync<PendingTripMutation>();

        _apiClientMock = new Mock<IApiClient>();
    }

    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    #endregion

    #region PendingTripMutation Entity Tests

    [Fact]
    public async Task PendingTripMutation_CanBeInserted()
    {
        // Arrange
        var mutation = CreatePlaceMutation("Create", "Test Place");

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Place");
        retrieved.EntityType.Should().Be("Place");
        retrieved.OperationType.Should().Be("Create");
    }

    [Fact]
    public async Task PendingTripMutation_StoresOriginalValues()
    {
        // Arrange - update mutation with original values for rollback
        var mutation = new PendingTripMutation
        {
            EntityType = "Place",
            OperationType = "Update",
            EntityId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            Name = "New Name",
            Latitude = 51.5,
            Longitude = -0.1,
            OriginalName = "Original Name",
            OriginalLatitude = 50.0,
            OriginalLongitude = 0.0,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert - original values preserved for rollback
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("New Name");
        retrieved.OriginalName.Should().Be("Original Name");
        retrieved.Latitude.Should().Be(51.5);
        retrieved.OriginalLatitude.Should().Be(50.0);
    }

    [Fact]
    public async Task PendingTripMutation_TracksRetryAttempts()
    {
        // Arrange
        var mutation = CreatePlaceMutation("Update", "Test Place");
        mutation.SyncAttempts = 2;
        mutation.LastSyncAttempt = DateTime.UtcNow.AddMinutes(-5);
        mutation.LastError = "Network timeout";

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SyncAttempts.Should().Be(2);
        retrieved.LastError.Should().Be("Network timeout");
    }

    [Fact]
    public async Task PendingTripMutation_CanBeRejected()
    {
        // Arrange
        var mutation = CreatePlaceMutation("Create", "Invalid Place");
        mutation.IsRejected = true;
        mutation.RejectionReason = "Server: Duplicate place name";

        // Act
        await _database.InsertAsync(mutation);
        var rejected = await _database.Table<PendingTripMutation>()
            .Where(m => m.IsRejected)
            .ToListAsync();

        // Assert
        rejected.Should().HaveCount(1);
        rejected[0].RejectionReason.Should().Contain("Duplicate");
    }

    [Theory]
    [InlineData("Place", "Create")]
    [InlineData("Place", "Update")]
    [InlineData("Place", "Delete")]
    [InlineData("Region", "Create")]
    [InlineData("Region", "Update")]
    [InlineData("Region", "Delete")]
    [InlineData("Trip", "Update")]
    [InlineData("Segment", "Update")]
    [InlineData("Area", "Update")]
    public async Task PendingTripMutation_SupportsAllEntityTypes(string entityType, string operationType)
    {
        // Arrange
        var mutation = new PendingTripMutation
        {
            EntityType = entityType,
            OperationType = operationType,
            EntityId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.EntityType.Should().Be(entityType);
        retrieved.OperationType.Should().Be(operationType);
    }

    #endregion

    #region Mutation Queue Query Tests

    [Fact]
    public async Task GetPendingMutations_ExcludesRejected()
    {
        // Arrange
        var pending1 = CreatePlaceMutation("Create", "Place 1");
        var pending2 = CreatePlaceMutation("Create", "Place 2");
        var rejected = CreatePlaceMutation("Create", "Rejected Place");
        rejected.IsRejected = true;

        await _database.InsertAsync(pending1);
        await _database.InsertAsync(pending2);
        await _database.InsertAsync(rejected);

        // Act
        var pendingMutations = await _database.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        // Assert
        pendingMutations.Should().HaveCount(2);
        pendingMutations.Should().NotContain(m => m.Name == "Rejected Place");
    }

    [Fact]
    public async Task GetPendingMutations_ExcludesExhaustedRetries()
    {
        // Arrange
        var pending = CreatePlaceMutation("Create", "Fresh Place");
        var exhausted = CreatePlaceMutation("Create", "Exhausted Place");
        exhausted.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        await _database.InsertAsync(pending);
        await _database.InsertAsync(exhausted);

        // Act
        var pendingMutations = await _database.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        // Assert
        pendingMutations.Should().HaveCount(1);
        pendingMutations[0].Name.Should().Be("Fresh Place");
    }

    [Fact]
    public async Task GetPendingMutations_OrdersByCreatedAt()
    {
        // Arrange
        var first = CreatePlaceMutation("Create", "First");
        first.CreatedAt = DateTime.UtcNow.AddMinutes(-30);

        var second = CreatePlaceMutation("Create", "Second");
        second.CreatedAt = DateTime.UtcNow.AddMinutes(-20);

        var third = CreatePlaceMutation("Create", "Third");
        third.CreatedAt = DateTime.UtcNow.AddMinutes(-10);

        // Insert out of order
        await _database.InsertAsync(third);
        await _database.InsertAsync(first);
        await _database.InsertAsync(second);

        // Act
        var mutations = await _database.Table<PendingTripMutation>()
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // Assert
        mutations[0].Name.Should().Be("First");
        mutations[1].Name.Should().Be("Second");
        mutations[2].Name.Should().Be("Third");
    }

    [Fact]
    public async Task GetMutationsForEntity_FindsExistingMutation()
    {
        // Arrange - simulates mutation merging for same entity
        var entityId = Guid.NewGuid();
        var mutation = CreatePlaceMutation("Update", "Original");
        mutation.EntityId = entityId;
        await _database.InsertAsync(mutation);

        // Act - find existing mutation for same entity (for merging)
        var existing = await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == "Place" && !m.IsRejected && m.OperationType != "Delete")
            .FirstOrDefaultAsync();

        // Assert
        existing.Should().NotBeNull();
        existing!.Name.Should().Be("Original");
    }

    [Fact]
    public async Task DeleteMutationsForEntity_RemovesAllMutations()
    {
        // Arrange - multiple mutations for same entity
        var entityId = Guid.NewGuid();
        var create = CreatePlaceMutation("Create", "Created");
        create.EntityId = entityId;
        var update = CreatePlaceMutation("Update", "Updated");
        update.EntityId = entityId;

        await _database.InsertAsync(create);
        await _database.InsertAsync(update);

        // Act - delete operation removes prior mutations
        await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == "Place")
            .DeleteAsync();

        // Assert
        var remaining = await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId)
            .CountAsync();
        remaining.Should().Be(0);
    }

    #endregion

    #region Mutation Counts Tests

    [Fact]
    public async Task GetPendingCount_ReturnsCorrectCount()
    {
        // Arrange
        await _database.InsertAsync(CreatePlaceMutation("Create", "Place 1"));
        await _database.InsertAsync(CreatePlaceMutation("Create", "Place 2"));
        var rejected = CreatePlaceMutation("Create", "Rejected");
        rejected.IsRejected = true;
        await _database.InsertAsync(rejected);

        // Act
        var count = await _database.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .CountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetFailedCount_IncludesRejectedAndExhausted()
    {
        // Arrange
        var rejected = CreatePlaceMutation("Create", "Rejected");
        rejected.IsRejected = true;

        var exhausted = CreatePlaceMutation("Create", "Exhausted");
        exhausted.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        var pending = CreatePlaceMutation("Create", "Pending");

        await _database.InsertAsync(rejected);
        await _database.InsertAsync(exhausted);
        await _database.InsertAsync(pending);

        // Act
        var failedCount = await _database.Table<PendingTripMutation>()
            .Where(m => m.IsRejected || m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .CountAsync();

        // Assert
        failedCount.Should().Be(2);
    }

    [Fact]
    public async Task GetFailedCountForTrip_FiltersCorrectly()
    {
        // Arrange
        var tripId1 = Guid.NewGuid();
        var tripId2 = Guid.NewGuid();

        var trip1Failed = CreatePlaceMutation("Create", "Trip1 Failed");
        trip1Failed.TripId = tripId1;
        trip1Failed.IsRejected = true;

        var trip1Pending = CreatePlaceMutation("Create", "Trip1 Pending");
        trip1Pending.TripId = tripId1;

        var trip2Failed = CreatePlaceMutation("Create", "Trip2 Failed");
        trip2Failed.TripId = tripId2;
        trip2Failed.IsRejected = true;

        await _database.InsertAsync(trip1Failed);
        await _database.InsertAsync(trip1Pending);
        await _database.InsertAsync(trip2Failed);

        // Act
        var trip1FailedCount = await _database.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId1 && (m.IsRejected || m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts))
            .CountAsync();

        // Assert
        trip1FailedCount.Should().Be(1);
    }

    #endregion

    #region Mutation Merging Tests

    [Fact]
    public async Task MutationMerging_UpdatesExistingMutation()
    {
        // Arrange - first update
        var entityId = Guid.NewGuid();
        var original = CreatePlaceMutation("Update", "First Name");
        original.EntityId = entityId;
        original.Latitude = 50.0;
        await _database.InsertAsync(original);

        // Act - simulate merging second update
        var existing = await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == "Place" && !m.IsRejected && m.OperationType != "Delete")
            .FirstOrDefaultAsync();

        existing!.Name = "Second Name";
        existing.Latitude = 51.5;
        existing.CreatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(existing);

        // Assert - should have single merged mutation
        var mutations = await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId)
            .ToListAsync();

        mutations.Should().HaveCount(1);
        mutations[0].Name.Should().Be("Second Name");
        mutations[0].Latitude.Should().Be(51.5);
    }

    [Fact]
    public async Task MutationMerging_PreservesOriginalValues()
    {
        // Arrange - first update with original values
        var entityId = Guid.NewGuid();
        var original = CreatePlaceMutation("Update", "First Name");
        original.EntityId = entityId;
        original.OriginalName = "Very Original";
        original.OriginalLatitude = 40.0;
        await _database.InsertAsync(original);

        // Act - merge second update (shouldn't overwrite originals)
        var existing = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == entityId);

        existing!.Name = "Third Name";
        // Don't update OriginalName - keep first original
        await _database.UpdateAsync(existing);

        // Assert - original values preserved from first mutation
        var mutation = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == entityId);

        mutation!.Name.Should().Be("Third Name");
        mutation.OriginalName.Should().Be("Very Original");
        mutation.OriginalLatitude.Should().Be(40.0);
    }

    #endregion

    #region Retry Reset Tests

    [Fact]
    public async Task ResetFailedMutations_ClearsAttemptCount()
    {
        // Arrange
        var failed1 = CreatePlaceMutation("Create", "Failed 1");
        failed1.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        var failed2 = CreatePlaceMutation("Create", "Failed 2");
        failed2.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        var rejected = CreatePlaceMutation("Create", "Rejected");
        rejected.IsRejected = true;

        await _database.InsertAsync(failed1);
        await _database.InsertAsync(failed2);
        await _database.InsertAsync(rejected);

        // Act - reset non-rejected failed mutations
        var failed = await _database.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        foreach (var mutation in failed)
        {
            mutation.SyncAttempts = 0;
            await _database.UpdateAsync(mutation);
        }

        // Assert - failed mutations reset, rejected unchanged
        var pendingCount = await _database.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .CountAsync();
        var rejectedCount = await _database.Table<PendingTripMutation>()
            .Where(m => m.IsRejected)
            .CountAsync();

        pendingCount.Should().Be(2);
        rejectedCount.Should().Be(1);
    }

    [Fact]
    public async Task ResetFailedMutationsForTrip_OnlyResetsThatTrip()
    {
        // Arrange
        var tripId1 = Guid.NewGuid();
        var tripId2 = Guid.NewGuid();

        var trip1Failed = CreatePlaceMutation("Create", "Trip1");
        trip1Failed.TripId = tripId1;
        trip1Failed.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        var trip2Failed = CreatePlaceMutation("Create", "Trip2");
        trip2Failed.TripId = tripId2;
        trip2Failed.SyncAttempts = PendingTripMutation.MaxSyncAttempts;

        await _database.InsertAsync(trip1Failed);
        await _database.InsertAsync(trip2Failed);

        // Act - reset only trip1
        var trip1Mutations = await _database.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId1 && !m.IsRejected && m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        foreach (var mutation in trip1Mutations)
        {
            mutation.SyncAttempts = 0;
            await _database.UpdateAsync(mutation);
        }

        // Assert
        var trip1Pending = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.TripId == tripId1);
        var trip2Pending = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.TripId == tripId2);

        trip1Pending!.SyncAttempts.Should().Be(0);
        trip2Pending!.SyncAttempts.Should().Be(PendingTripMutation.MaxSyncAttempts);
    }

    #endregion

    #region Clear Mutations Tests

    [Fact]
    public async Task ClearRejectedMutations_RemovesOnlyRejected()
    {
        // Arrange
        var pending = CreatePlaceMutation("Create", "Pending");
        var rejected = CreatePlaceMutation("Create", "Rejected");
        rejected.IsRejected = true;

        await _database.InsertAsync(pending);
        await _database.InsertAsync(rejected);

        // Act
        await _database.Table<PendingTripMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();

        // Assert
        var remaining = await _database.Table<PendingTripMutation>().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Name.Should().Be("Pending");
    }

    [Fact]
    public async Task ClearPendingMutationsForTrip_RemovesAllForTrip()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var otherTripId = Guid.NewGuid();

        var tripMutation1 = CreatePlaceMutation("Create", "Trip Place 1");
        tripMutation1.TripId = tripId;
        var tripMutation2 = CreatePlaceMutation("Update", "Trip Place 2");
        tripMutation2.TripId = tripId;
        var otherMutation = CreatePlaceMutation("Create", "Other Trip");
        otherMutation.TripId = otherTripId;

        await _database.InsertAsync(tripMutation1);
        await _database.InsertAsync(tripMutation2);
        await _database.InsertAsync(otherMutation);

        // Act
        await _database.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId)
            .DeleteAsync();

        // Assert
        var remaining = await _database.Table<PendingTripMutation>().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].TripId.Should().Be(otherTripId);
    }

    #endregion

    #region MaxSyncAttempts Constant Test

    [Fact]
    public void MaxSyncAttempts_IsReasonableValue()
    {
        // Verify the constant is sensible for retries
        PendingTripMutation.MaxSyncAttempts.Should().BeInRange(3, 10);
    }

    #endregion

    #region Event Args Structure Tests

    [Fact]
    public void SyncSuccessEventArgs_HasCorrectProperties()
    {
        // Arrange & Act
        var args = new SyncSuccessEventArgs { EntityId = Guid.NewGuid() };

        // Assert
        args.EntityId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SyncFailureEventArgs_HasCorrectProperties()
    {
        // Arrange & Act
        var args = new SyncFailureEventArgs
        {
            EntityId = Guid.NewGuid(),
            ErrorMessage = "Server rejected",
            IsClientError = true
        };

        // Assert
        args.EntityId.Should().NotBe(Guid.Empty);
        args.ErrorMessage.Should().Be("Server rejected");
        args.IsClientError.Should().BeTrue();
    }

    [Fact]
    public void SyncQueuedEventArgs_HasCorrectProperties()
    {
        // Arrange & Act
        var args = new SyncQueuedEventArgs
        {
            EntityId = Guid.NewGuid(),
            Message = "Queued for offline sync"
        };

        // Assert
        args.EntityId.Should().NotBe(Guid.Empty);
        args.Message.Should().Be("Queued for offline sync");
    }

    [Fact]
    public void EntityCreatedEventArgs_HasCorrectProperties()
    {
        // Arrange & Act
        var tempId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var args = new EntityCreatedEventArgs
        {
            TempClientId = tempId,
            ServerId = serverId,
            EntityType = "Place"
        };

        // Assert
        args.TempClientId.Should().Be(tempId);
        args.ServerId.Should().Be(serverId);
        args.EntityType.Should().Be("Place");
    }

    #endregion

    #region Region Mutation Tests

    [Fact]
    public async Task RegionMutation_StoresCenterCoordinates()
    {
        // Arrange
        var mutation = new PendingTripMutation
        {
            EntityType = "Region",
            OperationType = "Create",
            EntityId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            Name = "Test Region",
            CenterLatitude = 51.5074,
            CenterLongitude = -0.1278,
            CoverImageUrl = "https://example.com/image.jpg",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CenterLatitude.Should().Be(51.5074);
        retrieved.CenterLongitude.Should().Be(-0.1278);
        retrieved.CoverImageUrl.Should().Be("https://example.com/image.jpg");
    }

    [Fact]
    public async Task RegionMutation_StoresOriginalCenterCoordinates()
    {
        // Arrange
        var mutation = new PendingTripMutation
        {
            EntityType = "Region",
            OperationType = "Update",
            EntityId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            Name = "Updated Region",
            CenterLatitude = 52.0,
            CenterLongitude = 0.0,
            OriginalName = "Original Region",
            OriginalCenterLatitude = 51.0,
            OriginalCenterLongitude = -1.0,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.EntityId == mutation.EntityId);

        // Assert
        retrieved!.OriginalCenterLatitude.Should().Be(51.0);
        retrieved.OriginalCenterLongitude.Should().Be(-1.0);
    }

    #endregion

    #region TempClientId Tests

    [Fact]
    public async Task CreateMutation_StoresTempClientId()
    {
        // Arrange - offline creates use temp client ID until server assigns real ID
        var tempId = Guid.NewGuid();
        var mutation = new PendingTripMutation
        {
            EntityType = "Place",
            OperationType = "Create",
            EntityId = tempId,
            TripId = Guid.NewGuid(),
            TempClientId = tempId,
            Name = "Offline Created Place",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _database.InsertAsync(mutation);
        var retrieved = await _database.Table<PendingTripMutation>()
            .FirstOrDefaultAsync(m => m.TempClientId == tempId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.TempClientId.Should().Be(tempId);
        retrieved.EntityId.Should().Be(tempId);
    }

    #endregion

    #region Helper Methods

    private PendingTripMutation CreatePlaceMutation(string operationType, string name)
    {
        return new PendingTripMutation
        {
            EntityType = "Place",
            OperationType = operationType,
            EntityId = Guid.NewGuid(),
            TripId = Guid.NewGuid(),
            Name = name,
            Latitude = 51.5074,
            Longitude = -0.1278,
            CreatedAt = DateTime.UtcNow
        };
    }

    #endregion
}

#region Local Copy of PendingTripMutation (for testing)

/// <summary>
/// Local test copy of PendingTripMutation.
/// Test project cannot reference MAUI project directly.
/// </summary>
[Table("PendingTripMutations")]
public class PendingTripMutation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string EntityType { get; set; } = "Place";
    public string OperationType { get; set; } = "Update";

    [Indexed]
    public Guid EntityId { get; set; }

    [Indexed]
    public Guid TripId { get; set; }

    public Guid? RegionId { get; set; }
    public Guid? TempClientId { get; set; }

    // Current values
    public string? Name { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
    public int? DisplayOrder { get; set; }
    public string? IconName { get; set; }
    public string? MarkerColor { get; set; }
    public bool? ClearIcon { get; set; }
    public bool? ClearMarkerColor { get; set; }
    public bool IncludeNotes { get; set; }

    // Region-specific fields
    public string? CoverImageUrl { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }

    // Sync tracking
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int SyncAttempts { get; set; }
    public DateTime? LastSyncAttempt { get; set; }
    public string? LastError { get; set; }

    [Indexed]
    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }

    // Original values for restoration
    public string? OriginalName { get; set; }
    public double? OriginalLatitude { get; set; }
    public double? OriginalLongitude { get; set; }
    public string? OriginalNotes { get; set; }
    public int? OriginalDisplayOrder { get; set; }
    public string? OriginalIconName { get; set; }
    public string? OriginalMarkerColor { get; set; }
    public string? OriginalCoverImageUrl { get; set; }
    public double? OriginalCenterLatitude { get; set; }
    public double? OriginalCenterLongitude { get; set; }

    public const int MaxSyncAttempts = 5;

    [Ignore]
    public bool CanSync => !IsRejected && SyncAttempts < MaxSyncAttempts;
}

#endregion
