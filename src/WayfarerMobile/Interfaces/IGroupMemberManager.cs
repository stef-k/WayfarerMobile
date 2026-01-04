using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Manages group member operations (load, refresh, visibility, utilities).
/// </summary>
public interface IGroupMemberManager
{
    /// <summary>
    /// Loads members for a group and merges their latest locations.
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <returns>Tuple of (members list, current user's peer visibility disabled state).</returns>
    Task<(List<GroupMember> Members, bool MyPeerVisibilityDisabled)> LoadMembersWithLocationsAsync(Guid groupId);

    /// <summary>
    /// Refreshes member locations without full reload.
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="currentMembers">The current members collection to update.</param>
    /// <returns>Updated and sorted members list, or null on error.</returns>
    Task<List<GroupMember>?> RefreshMemberLocationsAsync(Guid groupId, IList<GroupMember> currentMembers);

    /// <summary>
    /// Updates the current user's peer visibility in a group.
    /// </summary>
    /// <param name="groupId">The group ID.</param>
    /// <param name="disabled">True to disable peer visibility, false to enable.</param>
    /// <returns>True if update succeeded.</returns>
    Task<bool> UpdatePeerVisibilityAsync(Guid groupId, bool disabled);

    /// <summary>
    /// Merges location data into member records.
    /// </summary>
    /// <param name="members">The members to update.</param>
    /// <param name="locations">The location data keyed by user ID.</param>
    void MergeLocationsIntoMembers(IList<GroupMember> members, Dictionary<string, MemberLocation> locations);

    /// <summary>
    /// Sorts members with live locations first, then alphabetically.
    /// </summary>
    /// <param name="members">The members to sort.</param>
    /// <returns>Sorted members list.</returns>
    List<GroupMember> SortMembersLiveFirst(IEnumerable<GroupMember> members);

    /// <summary>
    /// Finds a member by user ID.
    /// </summary>
    /// <param name="members">The members to search.</param>
    /// <param name="userId">The user ID to find.</param>
    /// <returns>The member, or null if not found.</returns>
    GroupMember? FindMemberByUserId(IEnumerable<GroupMember> members, string userId);

    /// <summary>
    /// Finds the current user (self) in the members list.
    /// </summary>
    /// <param name="members">The members to search.</param>
    /// <returns>The current user's member record, or null if not found.</returns>
    GroupMember? FindCurrentUser(IEnumerable<GroupMember> members);
}
