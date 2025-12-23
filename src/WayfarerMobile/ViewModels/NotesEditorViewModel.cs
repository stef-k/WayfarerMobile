using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Entity types supported by the notes editor.
/// </summary>
public enum NotesEntityType
{
    /// <summary>Timeline location notes.</summary>
    Timeline,
    /// <summary>Trip notes.</summary>
    Trip,
    /// <summary>Region notes.</summary>
    Region,
    /// <summary>Place notes.</summary>
    Place,
    /// <summary>Segment notes.</summary>
    Segment,
    /// <summary>Area (geographic polygon) notes.</summary>
    Area
}

/// <summary>
/// ViewModel for the notes editor page.
/// Supports editing notes for timeline locations, trips, regions, places, and segments.
/// </summary>
public partial class NotesEditorViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly ITripSyncService _tripSyncService;
    private readonly TripDownloadService _downloadService;
    private readonly IToastService _toastService;
    private readonly ISettingsService _settingsService;
    private string? _originalNotesHtml;

    /// <summary>
    /// Gets or sets the entity type being edited.
    /// </summary>
    [ObservableProperty]
    private NotesEntityType _entityType = NotesEntityType.Timeline;

    /// <summary>
    /// Gets or sets the location ID being edited (for Timeline entity type).
    /// </summary>
    [ObservableProperty]
    private int _locationId;

    /// <summary>
    /// Gets or sets the entity ID being edited (for Trip, Region, Place, Segment).
    /// </summary>
    [ObservableProperty]
    private Guid _entityId;

    /// <summary>
    /// Gets or sets the trip ID (for Region, Place, Segment).
    /// </summary>
    [ObservableProperty]
    private Guid _tripId;

    /// <summary>
    /// Gets or sets the current notes HTML content.
    /// </summary>
    [ObservableProperty]
    private string? _notesHtml;

    /// <summary>
    /// Gets or sets whether the editor is ready.
    /// </summary>
    [ObservableProperty]
    private bool _isEditorReady;

    /// <summary>
    /// Gets or sets whether saving is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Gets or sets whether there are unsaved changes.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Creates a new instance of NotesEditorViewModel.
    /// </summary>
    public NotesEditorViewModel(
        ITimelineSyncService timelineSyncService,
        ITripSyncService tripSyncService,
        TripDownloadService downloadService,
        IToastService toastService,
        ISettingsService settingsService)
    {
        _timelineSyncService = timelineSyncService;
        _tripSyncService = tripSyncService;
        _downloadService = downloadService;
        _toastService = toastService;
        _settingsService = settingsService;
        Title = "Edit Notes";
    }

    /// <summary>
    /// Applies query attributes from navigation.
    /// </summary>
    /// <param name="query">The query parameters.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Parse entity type (defaults to Timeline for backward compatibility)
        if (query.TryGetValue("entityType", out var entityTypeObj))
        {
            if (Enum.TryParse<NotesEntityType>(entityTypeObj?.ToString(), out var entityType))
            {
                EntityType = entityType;
            }
        }

        // Parse location ID (for Timeline entity type)
        if (query.TryGetValue("locationId", out var locationIdObj))
        {
            if (int.TryParse(locationIdObj?.ToString(), out var locationId))
            {
                LocationId = locationId;
            }
        }

        // Parse trip ID (for Trip, Region, Place, Segment - used as parent reference)
        if (query.TryGetValue("tripId", out var tripIdObj))
        {
            if (Guid.TryParse(tripIdObj?.ToString(), out var tripId))
            {
                TripId = tripId;
                // For Trip entity type, the tripId IS the entity being edited
                if (EntityType == NotesEntityType.Trip)
                {
                    EntityId = tripId;
                }
            }
        }

        // Parse entity ID (for Region, Place, Segment)
        if (query.TryGetValue("entityId", out var entityIdObj))
        {
            if (Guid.TryParse(entityIdObj?.ToString(), out var entityId))
            {
                EntityId = entityId;
            }
        }

        // Parse notes content
        if (query.TryGetValue("notes", out var notesObj))
        {
            NotesHtml = notesObj?.ToString();
            _originalNotesHtml = NotesHtml;
        }
    }

    /// <summary>
    /// Gets the HTML for the Quill editor.
    /// Converts image URLs to proxy URLs for WebView display.
    /// </summary>
    public async Task<string> GetEditorHtmlAsync()
    {
        try
        {
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync("editor/notes-editor-quill.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            // Convert images to proxy URLs for WebView display
            var contentWithProxyUrls = ImageProxyHelper.ConvertImagesToProxyUrls(
                NotesHtml ?? string.Empty,
                _settingsService.ServerUrl);

            var json = JsonSerializer.Serialize(contentWithProxyUrls);
            html = html.Replace("__WF_INITIAL_CONTENT__", json);

            return html;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditorViewModel] Failed to load editor: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Sets the current content from the WebView.
    /// </summary>
    /// <param name="html">The HTML content.</param>
    public void SetCurrentContent(string? html)
    {
        NotesHtml = html;
        HasChanges = !string.Equals(NormalizeHtml(_originalNotesHtml), NormalizeHtml(html), StringComparison.Ordinal);
    }

    /// <summary>
    /// Saves the notes.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Validate we have required IDs for the entity type
        if (!ValidateEntityIds())
        {
            return;
        }

        try
        {
            IsSaving = true;

            // Prepare notes content
            var notesToSave = PrepareNotesForSave(NotesHtml);

            // Save based on entity type
            switch (EntityType)
            {
                case NotesEntityType.Timeline:
                    await SaveTimelineNotesAsync(notesToSave);
                    break;

                case NotesEntityType.Trip:
                    await SaveTripNotesAsync(notesToSave);
                    break;

                case NotesEntityType.Region:
                    await SaveRegionNotesAsync(notesToSave);
                    break;

                case NotesEntityType.Place:
                    await SavePlaceNotesAsync(notesToSave);
                    break;

                case NotesEntityType.Segment:
                    await SaveSegmentNotesAsync(notesToSave);
                    break;

                case NotesEntityType.Area:
                    await SaveAreaNotesAsync(notesToSave);
                    break;
            }

            _originalNotesHtml = NotesHtml;
            HasChanges = false;

            await _toastService.ShowSuccessAsync("Notes saved");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditorViewModel] Save error: {ex.Message}");
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Validates that required entity IDs are set for the current entity type.
    /// </summary>
    private bool ValidateEntityIds()
    {
        return EntityType switch
        {
            NotesEntityType.Timeline => LocationId != 0,
            NotesEntityType.Trip => EntityId != Guid.Empty,
            NotesEntityType.Region => EntityId != Guid.Empty && TripId != Guid.Empty,
            NotesEntityType.Place => EntityId != Guid.Empty && TripId != Guid.Empty,
            NotesEntityType.Segment => EntityId != Guid.Empty && TripId != Guid.Empty,
            NotesEntityType.Area => EntityId != Guid.Empty && TripId != Guid.Empty,
            _ => false
        };
    }

    /// <summary>
    /// Prepares notes HTML for saving by cleaning empty content and converting proxy URLs.
    /// </summary>
    private string? PrepareNotesForSave(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        // Treat visually empty content as null (but preserve images)
        var plain = Regex.Replace(html, "<[^>]+>", " ");
        var hasImages = Regex.IsMatch(html, @"<img\s", RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(plain) && !hasImages)
        {
            return null;
        }

        // Convert proxy URLs back to original URLs for server storage
        return ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, _settingsService.ServerUrl);
    }

    /// <summary>
    /// Saves timeline location notes.
    /// </summary>
    private async Task SaveTimelineNotesAsync(string? notes)
    {
        await _timelineSyncService.UpdateLocationAsync(
            LocationId,
            latitude: null,
            longitude: null,
            localTimestamp: null,
            notes: notes,
            includeNotes: true);
    }

    /// <summary>
    /// Saves trip notes.
    /// </summary>
    private async Task SaveTripNotesAsync(string? notes)
    {
        // Update local database optimistically
        await _downloadService.UpdateTripNotesAsync(EntityId, notes);

        // Queue server sync (includeNotes: true ensures notes are sent to server)
        await _tripSyncService.UpdateTripAsync(EntityId, name: null, notes: notes, includeNotes: true);
    }

    /// <summary>
    /// Saves region notes.
    /// </summary>
    private async Task SaveRegionNotesAsync(string? notes)
    {
        // Queue server sync
        await _tripSyncService.UpdateRegionAsync(
            EntityId,
            TripId,
            notes: notes,
            includeNotes: true);
    }

    /// <summary>
    /// Saves place notes.
    /// </summary>
    private async Task SavePlaceNotesAsync(string? notes)
    {
        // Queue server sync
        await _tripSyncService.UpdatePlaceAsync(
            EntityId,
            TripId,
            notes: notes,
            includeNotes: true);
    }

    /// <summary>
    /// Saves segment notes.
    /// </summary>
    private async Task SaveSegmentNotesAsync(string? notes)
    {
        // Queue server sync (segments don't have local storage)
        // Parameters: segmentId, tripId, notes
        await _tripSyncService.UpdateSegmentNotesAsync(EntityId, TripId, notes);
    }

    /// <summary>
    /// Saves area notes.
    /// </summary>
    private async Task SaveAreaNotesAsync(string? notes)
    {
        // Queue server sync (areas don't have local storage)
        await _tripSyncService.UpdateAreaNotesAsync(TripId, EntityId, notes);
    }

    /// <summary>
    /// Cancels editing and navigates back.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        if (HasChanges)
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                var discard = await page.DisplayAlertAsync(
                    "Unsaved Changes",
                    "You have unsaved changes. Discard them?",
                    "Discard",
                    "Keep Editing");

                if (!discard)
                {
                    return;
                }
            }
        }

        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// Normalizes HTML for comparison.
    /// </summary>
    private static string NormalizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        try
        {
            var normalized = Regex.Replace(html, @">\s+<", "><");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Trim();
        }
        catch
        {
            return html.Trim();
        }
    }
}
