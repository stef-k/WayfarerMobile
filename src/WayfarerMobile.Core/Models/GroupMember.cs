using System.ComponentModel;

namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a member of a group.
/// </summary>
public class GroupMember : INotifyPropertyChanged
{
    private bool _isVisibleOnMap = true;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the group role (Owner, Manager, Member).
    /// </summary>
    public string GroupRole { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the membership status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the member's color (hex format).
    /// </summary>
    public string? ColorHex { get; set; }

    /// <summary>
    /// Gets or sets whether this is the current user.
    /// </summary>
    public bool IsSelf { get; set; }

    /// <summary>
    /// Gets or sets the SSE channel for location updates.
    /// </summary>
    public string? SseChannel { get; set; }

    /// <summary>
    /// Gets or sets whether peer visibility is disabled for this member.
    /// </summary>
    public bool OrgPeerVisibilityAccessDisabled { get; set; }

    /// <summary>
    /// Gets or sets whether this member is visible on the map.
    /// </summary>
    public bool IsVisibleOnMap
    {
        get => _isVisibleOnMap;
        set
        {
            if (_isVisibleOnMap != value)
            {
                _isVisibleOnMap = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisibleOnMap)));
            }
        }
    }

    /// <summary>
    /// Gets the display name to show (falls back to username).
    /// </summary>
    public string DisplayText => !string.IsNullOrEmpty(DisplayName) ? DisplayName : UserName;

    /// <summary>
    /// Gets or sets the member's last known location.
    /// </summary>
    public MemberLocation? LastLocation { get; set; }
}

/// <summary>
/// Represents a member's location.
/// </summary>
public class MemberLocation
{
    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets whether the location is considered live.
    /// </summary>
    public bool IsLive { get; set; }

    /// <summary>
    /// Gets or sets the location address if available.
    /// </summary>
    public string? Address { get; set; }
}
