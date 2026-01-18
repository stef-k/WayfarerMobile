using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Manages group member operations (load, refresh, visibility, utilities).
/// </summary>
public class GroupMemberManager : IGroupMemberManager
{
    private readonly IGroupsService _groupsService;
    private readonly ILogger<GroupMemberManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupMemberManager"/> class.
    /// </summary>
    public GroupMemberManager(
        IGroupsService groupsService,
        ILogger<GroupMemberManager> logger)
    {
        _groupsService = groupsService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(List<GroupMember> Members, bool MyPeerVisibilityDisabled)> LoadMembersWithLocationsAsync(Guid groupId)
    {
        // Load members
        var members = await _groupsService.GetGroupMembersAsync(groupId);
        _logger.LogInformation("[GroupMemberManager] Loaded {Count} members for group {GroupId}", members.Count, groupId);

        // Load latest locations
        var locations = await _groupsService.GetLatestLocationsAsync(groupId);
        _logger.LogInformation("[GroupMemberManager] Loaded {Count} locations for group {GroupId}", locations.Count, groupId);

        // Find current user and get visibility state
        var currentUser = FindCurrentUser(members);
        var myPeerVisibilityDisabled = currentUser?.OrgPeerVisibilityAccessDisabled ?? false;

        // Merge locations into members
        MergeLocationsIntoMembers(members, locations);

        // Sort members (live first, then alphabetically)
        var sortedMembers = SortMembersLiveFirst(members);

        return (sortedMembers, myPeerVisibilityDisabled);
    }

    /// <inheritdoc/>
    public async Task<List<GroupMember>?> RefreshMemberLocationsAsync(Guid groupId, IList<GroupMember> currentMembers)
    {
        try
        {
            var locations = await _groupsService.GetLatestLocationsAsync(groupId).ConfigureAwait(false);

            // Update existing members with new locations
            MergeLocationsIntoMembers(currentMembers, locations);

            // Return sorted list
            return SortMembersLiveFirst(currentMembers);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error refreshing locations for group {GroupId}: {Message}", groupId, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Request timed out refreshing locations for group {GroupId}", groupId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error refreshing locations for group {GroupId}", groupId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdatePeerVisibilityAsync(Guid groupId, bool disabled)
    {
        try
        {
            _logger.LogInformation("[GroupMemberManager] Updating peer visibility: disabled={Disabled}", disabled);

            var success = await _groupsService.UpdatePeerVisibilityAsync(groupId, disabled);

            if (success)
            {
                _logger.LogInformation("[GroupMemberManager] Peer visibility updated successfully: disabled={Disabled}", disabled);
            }
            else
            {
                _logger.LogWarning("[GroupMemberManager] Failed to update peer visibility");
            }

            return success;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error toggling peer visibility for group {GroupId}: {Message}", groupId, ex.Message);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out toggling peer visibility for group {GroupId}", groupId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error toggling peer visibility for group {GroupId}", groupId);
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Thread safety note: This method mutates individual member properties, not the collection itself.
    /// Property assignment for reference types is atomic. Callers should marshal to the UI thread
    /// after calling this method if the updated members will be accessed by UI bindings.
    /// </remarks>
    public void MergeLocationsIntoMembers(IList<GroupMember> members, Dictionary<string, MemberLocation> locations)
    {
        foreach (var member in members)
        {
            if (locations.TryGetValue(member.UserId, out var location))
            {
                member.LastLocation = location;
            }
        }
    }

    /// <inheritdoc/>
    public List<GroupMember> SortMembersLiveFirst(IEnumerable<GroupMember> members)
    {
        return members
            .OrderByDescending(m => m.LastLocation?.IsLive ?? false)
            .ThenBy(m => m.DisplayText)
            .ToList();
    }

    /// <inheritdoc/>
    public GroupMember? FindMemberByUserId(IEnumerable<GroupMember> members, string userId)
    {
        return members.FirstOrDefault(m => m.UserId == userId);
    }

    /// <inheritdoc/>
    public GroupMember? FindCurrentUser(IEnumerable<GroupMember> members)
    {
        return members.FirstOrDefault(m => m.IsSelf);
    }
}
