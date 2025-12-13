using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents an activity type for check-ins.
/// Cached locally from server with fallback to defaults.
/// </summary>
[Table("ActivityTypes")]
public class ActivityType
{
    /// <summary>
    /// Gets or sets the activity ID.
    /// Positive IDs are from server, negative IDs are local defaults.
    /// </summary>
    [PrimaryKey]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the activity name.
    /// </summary>
    [NotNull]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the activity description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the icon name for UI display.
    /// </summary>
    public string IconName { get; set; } = "marker";

    /// <summary>
    /// Gets or sets the last sync timestamp.
    /// </summary>
    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets whether this is a server-provided activity (positive ID).
    /// </summary>
    [Ignore]
    public bool IsServerActivity => Id > 0;

    /// <summary>
    /// Gets whether this is a default/fallback activity (negative ID).
    /// </summary>
    [Ignore]
    public bool IsDefaultActivity => Id < 0;
}
