namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for DateTimeEditorViewModel to access parent state.
/// </summary>
public interface IDateTimeEditorCallbacks
{
    /// <summary>
    /// Gets the currently selected location.
    /// </summary>
    TimelineLocationDisplay? SelectedLocation { get; }

    /// <summary>
    /// Gets whether the app is currently online.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Gets or sets whether the view model is busy.
    /// </summary>
    bool IsBusy { get; set; }

    /// <summary>
    /// Reloads timeline data after datetime save.
    /// </summary>
    Task ReloadTimelineAsync();

    /// <summary>
    /// Shows location details sheet for a specific location.
    /// </summary>
    /// <param name="locationId">The location ID to show.</param>
    void ShowLocationDetails(int locationId);

    /// <summary>
    /// Opens the location sheet.
    /// </summary>
    void OpenLocationSheet();
}
