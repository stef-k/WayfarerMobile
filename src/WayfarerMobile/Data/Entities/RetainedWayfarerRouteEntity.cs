using SQLite;

namespace WayfarerMobile.Data.Entities;

[Table("RetainedWayfarerRoutes")]
public sealed class RetainedWayfarerRouteEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }
    [Indexed]
    public string NormalizedServer { get; set; } = string.Empty;
    [Indexed]
    public string AccountPartition { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ProviderConfigurationId { get; set; } = string.Empty;
    public string MappingIdentity { get; set; } = string.Empty;
    [Indexed]
    public string TransportProfileId { get; set; } = string.Empty;
    public string SelectedProfileAuthorityIdentity { get; set; } = string.Empty;
    public string? ProviderMode { get; set; }
    public string ModeKey { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int OriginLongitude { get; set; }
    public int OriginLatitude { get; set; }
    public int DestinationLongitude { get; set; }
    public int DestinationLatitude { get; set; }
    public string AnchorsKey { get; set; } = string.Empty;
    public string GeometryJson { get; set; } = string.Empty;
    public string InstructionsJson { get; set; } = string.Empty;
    public double DistanceMetres { get; set; }
    public double DurationSeconds { get; set; }
    public long StoredAtUnixMilliseconds { get; set; }
    public long LastUsedAtUnixMilliseconds { get; set; }
    public long GeneratedAtUnixMilliseconds { get; set; }
    public string StorageAuthority { get; set; } = string.Empty;
    public string AttributionJson { get; set; } = string.Empty;
    public bool IsCurrentAuthority { get; set; }
}
