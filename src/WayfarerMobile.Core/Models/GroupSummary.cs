namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a group summary from the server.
/// </summary>
public class GroupSummary
{
    /// <summary>
    /// Gets or sets the unique identifier of the group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the group name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the group description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the group type (e.g., Friends, Organization).
    /// </summary>
    public string? GroupType { get; set; }

    /// <summary>
    /// Gets or sets the number of active members.
    /// </summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// Gets or sets whether the current user owns this group.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// Gets or sets whether the current user is a manager.
    /// </summary>
    public bool IsManager { get; set; }

    /// <summary>
    /// Gets or sets whether the current user is a member.
    /// </summary>
    public bool IsMember { get; set; }

    /// <summary>
    /// Gets or sets whether org peer visibility is enabled.
    /// </summary>
    public bool OrgPeerVisibilityEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the user has peer visibility access.
    /// </summary>
    public bool HasOrgPeerVisibilityAccess { get; set; }

    /// <summary>
    /// Gets the role display text.
    /// </summary>
    public string RoleText => IsOwner ? "Owner" : IsManager ? "Manager" : "Member";
}
