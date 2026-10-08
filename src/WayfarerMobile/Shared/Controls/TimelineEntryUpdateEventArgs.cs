using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Event arguments for timeline entry update requests.
/// </summary>
public class TimelineEntryUpdateEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the originating record identity; a numeric display ID grants no authority.
    /// </summary>
    public TimelineEntryIdentity Identity { get; set; } = TimelineEntryIdentity.Unknown;

    /// <summary>
    /// Gets or sets the new latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the new longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the new local timestamp.
    /// </summary>
    public DateTime LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the new notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the activity type ID (null if not changed).
    /// </summary>
    public int? ActivityTypeId { get; set; }

    /// <summary>
    /// Gets or sets whether the activity was cleared (true to remove activity).
    /// </summary>
    public bool ClearActivity { get; set; }
}
