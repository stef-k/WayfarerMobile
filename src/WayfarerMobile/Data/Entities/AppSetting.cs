using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a key-value application setting stored locally.
/// </summary>
[Table("AppSettings")]
public class AppSetting
{
    /// <summary>
    /// Gets or sets the setting key (unique identifier).
    /// </summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the setting value as a string.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets when this setting was last modified.
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
