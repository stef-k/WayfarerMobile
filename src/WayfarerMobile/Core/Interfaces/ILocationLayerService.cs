using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing the current user's location indicator on the map.
/// Handles the blue dot, accuracy circle, heading cone, and pulsing animation.
/// </summary>
public interface ILocationLayerService : IDisposable
{
    /// <summary>Layer name for current location indicator.</summary>
    string LocationLayerName { get; }

    /// <summary>
    /// Gets whether the current location is stale (no GPS updates recently).
    /// </summary>
    bool IsLocationStale { get; }

    /// <summary>
    /// Gets the time since last location update in seconds.
    /// </summary>
    double SecondsSinceLastUpdate { get; }

    /// <summary>
    /// Updates the current location indicator on the map.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="location">The current location.</param>
    void UpdateLocation(WritableLayer layer, LocationData location);

    /// <summary>
    /// Clears the location indicator features from the layer.
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearLocation(WritableLayer layer);

    /// <summary>
    /// Shows the last known location with a gray indicator (GPS unavailable/stale).
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <returns>True if last known location was displayed, false if none available.</returns>
    bool ShowLastKnownLocation(WritableLayer layer);

    /// <summary>
    /// Starts the pulsing animation for the location indicator.
    /// Call this when navigation starts or tracking is active.
    /// </summary>
    /// <param name="layer">The layer containing the indicator.</param>
    /// <param name="onTick">Callback to refresh the layer on each tick.</param>
    void StartAnimation(WritableLayer layer, Action onTick);

    /// <summary>
    /// Stops the pulsing animation.
    /// </summary>
    void StopAnimation();

    /// <summary>
    /// Sets the navigation route state (affects indicator color).
    /// </summary>
    /// <param name="isOnRoute">Whether currently on the navigation route.</param>
    void SetNavigationState(bool isOnRoute);

    /// <summary>
    /// Gets the last known map point (for animation updates).
    /// </summary>
    MPoint? LastMapPoint { get; }

    /// <summary>
    /// Gets the last known accuracy (for animation updates).
    /// </summary>
    double LastAccuracy { get; }

    /// <summary>
    /// Gets the last known heading (for animation updates).
    /// </summary>
    double LastHeading { get; }
}
