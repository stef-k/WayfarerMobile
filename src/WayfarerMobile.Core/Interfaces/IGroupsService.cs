using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing groups and group member locations.
/// </summary>
public interface IGroupsService
{
    /// <summary>
    /// Gets the list of groups the current user belongs to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of group summaries.</returns>
    Task<List<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the members of a specific group.
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of group members.</returns>
    Task<List<GroupMember>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest locations for members of a group.
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="userIds">Optional list of specific user IDs to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of user ID to their latest location.</returns>
    Task<Dictionary<string, MemberLocation>> GetLatestLocationsAsync(
        Guid groupId,
        List<string>? userIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries locations within viewport bounds for historical viewing.
    /// POST /api/mobile/groups/{groupId}/locations/query
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="request">Query request with bounds, date filter, and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query response with pagination metadata and results, or null if request failed.</returns>
    Task<GroupLocationsQueryResponse?> QueryLocationsAsync(
        Guid groupId,
        GroupLocationsQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's peer visibility setting.
    /// PATCH /api/mobile/groups/{groupId}/peer-visibility
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="disabled">Whether peer visibility should be disabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the update succeeded, false otherwise.</returns>
    Task<bool> UpdatePeerVisibilityAsync(
        Guid groupId,
        bool disabled,
        CancellationToken cancellationToken = default);
}
