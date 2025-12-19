namespace WayfarerMobile.Core.Models;

/// <summary>
/// Helper class for Mapsui 5.0 layer metadata.
/// Replaces the removed IsMapInfoLayer property - use this as Layer.Tag.
/// </summary>
public class MapLayerData
{
    /// <summary>
    /// Indicates whether this layer should be included in MapInfo queries.
    /// Set this to true for layers that should respond to map taps/clicks.
    /// </summary>
    public bool IsMapInfoLayer { get; set; }
}
