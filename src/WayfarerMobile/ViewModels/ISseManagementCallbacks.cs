using WayfarerMobile.Core.Models;
using WayfarerMobile.Shared.Collections;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for SseManagementViewModel to access state and operations from GroupsViewModel.
/// Enables SSE operations without tight coupling to the parent ViewModel.
/// </summary>
public interface ISseManagementCallbacks
{
    #region State Properties

    /// <summary>
    /// Gets whether the selected date is today (live mode).
    /// SSE events are only processed when viewing today's data.
    /// </summary>
    bool IsToday { get; }

    /// <summary>
    /// Gets the currently selected group ID.
    /// </summary>
    Guid? SelectedGroupId { get; }

    /// <summary>
    /// Gets the members collection for the selected group.
    /// </summary>
    ObservableRangeCollection<GroupMember> Members { get; }

    /// <summary>
    /// Gets or sets whether the current user's peer visibility is disabled.
    /// </summary>
    bool MyPeerVisibilityDisabled { get; set; }

    /// <summary>
    /// Gets whether the ViewModel has been disposed.
    /// Used to guard against events firing after disposal.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets whether map view is currently active.
    /// </summary>
    bool IsMapView { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Updates map markers with current member locations.
    /// Called after SSE location updates.
    /// </summary>
    void UpdateMapMarkers();

    #endregion

    #region Data Operations

    /// <summary>
    /// Loads members for the selected group.
    /// Called when a new member joins via SSE.
    /// </summary>
    Task LoadMembersAsync();

    #endregion
}
