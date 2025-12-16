using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the notes editor page.
/// </summary>
public partial class NotesEditorViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private readonly ISettingsService _settingsService;
    private string? _originalNotesHtml;

    /// <summary>
    /// Gets or sets the location ID being edited.
    /// </summary>
    [ObservableProperty]
    private int _locationId;

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
    /// <param name="timelineSyncService">The timeline sync service.</param>
    /// <param name="toastService">The toast service.</param>
    /// <param name="settingsService">The settings service.</param>
    public NotesEditorViewModel(
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ISettingsService settingsService)
    {
        _timelineSyncService = timelineSyncService;
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
        if (query.TryGetValue("locationId", out var locationIdObj))
        {
            if (int.TryParse(locationIdObj?.ToString(), out var locationId))
            {
                LocationId = locationId;
            }
        }

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
        if (LocationId == 0) return;

        try
        {
            IsSaving = true;

            // Treat visually empty content as empty string
            var notesToSave = NotesHtml;
            if (!string.IsNullOrEmpty(notesToSave))
            {
                var plain = Regex.Replace(notesToSave, "<[^>]+>", " ");
                if (string.IsNullOrWhiteSpace(plain))
                {
                    notesToSave = null;
                }
            }

            // Convert proxy URLs back to original URLs for server storage
            if (!string.IsNullOrEmpty(notesToSave))
            {
                notesToSave = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(
                    notesToSave,
                    _settingsService.ServerUrl);
            }

            await _timelineSyncService.UpdateLocationAsync(
                LocationId,
                latitude: null,
                longitude: null,
                localTimestamp: null,
                notes: notesToSave,
                includeNotes: true);

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
