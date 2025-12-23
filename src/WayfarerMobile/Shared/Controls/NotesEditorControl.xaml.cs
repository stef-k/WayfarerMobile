using System.Text.Json;
using System.Text.RegularExpressions;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Reusable control for displaying and editing rich HTML notes using Quill.js editor.
/// Supports both viewer mode (read-only display) and editor mode (modal overlay with Quill).
/// </summary>
public partial class NotesEditorControl : ContentView
{
    private bool _isNotesModalOpen;
    private string? _originalNotesHtml;

    #region Events

    /// <summary>
    /// Event raised when notes are saved from the editor.
    /// Passes the new HTML notes content.
    /// </summary>
    public event EventHandler<string>? NotesSaved;

    /// <summary>
    /// Event raised when the editor is closed without saving.
    /// </summary>
    public event EventHandler? EditorClosed;

    #endregion

    #region Bindable Properties

    /// <summary>
    /// Gets or sets the title displayed in the editor modal header.
    /// </summary>
    public static readonly BindableProperty EditorTitleProperty =
        BindableProperty.Create(nameof(EditorTitle), typeof(string), typeof(NotesEditorControl), "Notes",
            propertyChanged: OnEditorTitleChanged);

    /// <summary>
    /// Gets or sets the editor title.
    /// </summary>
    public string EditorTitle
    {
        get => (string)GetValue(EditorTitleProperty);
        set => SetValue(EditorTitleProperty, value);
    }

    private static void OnEditorTitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NotesEditorControl control && newValue is string title)
        {
            control.ModalTitleLabel.Text = title;
        }
    }

    /// <summary>
    /// Gets or sets the backend base URL for image proxy conversion.
    /// When set, images in notes will be proxied through the backend for proper display.
    /// </summary>
    public static readonly BindableProperty BackendBaseUrlProperty =
        BindableProperty.Create(nameof(BackendBaseUrl), typeof(string), typeof(NotesEditorControl), null);

    /// <summary>
    /// Gets or sets the backend base URL for image proxy conversion.
    /// </summary>
    public string? BackendBaseUrl
    {
        get => (string?)GetValue(BackendBaseUrlProperty);
        set => SetValue(BackendBaseUrlProperty, value);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Check if the editor is currently open.
    /// </summary>
    public bool IsEditorOpen => _isNotesModalOpen;

    /// <summary>
    /// Gets the effective backend URL for image proxy conversion.
    /// Returns the explicitly set BackendBaseUrl, or resolves from ISettingsService if not set.
    /// </summary>
    private string? EffectiveBackendUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(BackendBaseUrl))
            {
                return BackendBaseUrl;
            }

            // Fallback: resolve from ISettingsService
            try
            {
                var settingsService = Application.Current?.Handler?.MauiContext?.Services
                    .GetService<ISettingsService>();
                return settingsService?.ServerUrl;
            }
            catch
            {
                return null;
            }
        }
    }

    #endregion

    /// <summary>
    /// Creates a new instance of NotesEditorControl.
    /// </summary>
    public NotesEditorControl()
    {
        InitializeComponent();
    }

    #region Public Methods

    /// <summary>
    /// Show the notes viewer with HTML content (read-only display).
    /// </summary>
    public async Task ShowViewerAsync(string? notesHtml)
    {
        await LoadNotesViewerAsync(notesHtml);
        NotesWebView.IsVisible = true;
    }

    /// <summary>
    /// Hide the notes viewer.
    /// </summary>
    public void HideViewer()
    {
        NotesWebView.IsVisible = false;
    }

    /// <summary>
    /// Open the notes editor modal with initial HTML content.
    /// </summary>
    public async Task ShowEditorAsync(string? initialHtml)
    {
        await OpenNotesEditorModalAsync(initialHtml);
    }

    /// <summary>
    /// Get the currently edited HTML from the editor (without saving).
    /// </summary>
    public async Task<string> GetCurrentEditorContentAsync()
    {
        if (!_isNotesModalOpen)
        {
            return string.Empty;
        }

        return await GetEditedNotesHtmlFromModalAsync();
    }

    #endregion

    #region Private Implementation

    /// <summary>
    /// Open the notes editor modal overlay for editing notes.
    /// </summary>
    private async Task OpenNotesEditorModalAsync(string? initialHtml)
    {
        try
        {
            await LoadNotesEditorModalAsync(initialHtml);

            _isNotesModalOpen = true;
            NotesEditorModalOverlay.IsVisible = true;
            NotesEditorModalOverlay.InputTransparent = false;

            await Task.Delay(500);

            try
            {
                var renderedContent = await GetEditedNotesHtmlFromModalAsync();
                _originalNotesHtml = NormalizeHtml(renderedContent);
            }
            catch
            {
                _originalNotesHtml = NormalizeHtml(initialHtml ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditor] Failed to open: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the HTML editor into the modal WebView.
    /// </summary>
    private async Task LoadNotesEditorModalAsync(string? initialHtml)
    {
        try
        {
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync("editor/notes-editor-quill.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            // Convert images to proxy URLs for WebView display
            var processedContent = ImageProxyHelper.ConvertImagesToProxyUrls(initialHtml, EffectiveBackendUrl);

            var json = JsonSerializer.Serialize(processedContent);
            html = html.Replace("__WF_INITIAL_CONTENT__", json);

            var src = new HtmlWebViewSource { Html = html };
            NotesWebViewModal.Source = src;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditor] Failed to load editor: {ex.Message}");
        }
    }

    /// <summary>
    /// Load the HTML viewer into the notes WebView for read-only display.
    /// </summary>
    private async Task LoadNotesViewerAsync(string? notesHtml)
    {
        try
        {
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync("viewer/notes-viewer.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            // Convert images to proxy URLs for WebView display
            var processedContent = ImageProxyHelper.ConvertImagesToProxyUrls(notesHtml, EffectiveBackendUrl);

            var json = JsonSerializer.Serialize(processedContent);
            html = html.Replace("__WF_INITIAL_CONTENT__", json);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var src = new HtmlWebViewSource { Html = html };
                NotesWebView.Source = src;
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        var jsHeight = @"
                            var viewer = document.getElementById('viewer');
                            Math.max(viewer.scrollHeight, viewer.offsetHeight, document.documentElement.scrollHeight);
                        ";
                        var heightResult = await NotesWebView.EvaluateJavaScriptAsync(jsHeight);

                        if (double.TryParse(heightResult?.ToString(), out var contentHeight))
                        {
                            NotesWebView.HeightRequest = Math.Min(contentHeight + 50, 800);
                        }
                    }
                    catch { }
                });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditor] Failed to load viewer: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle WebView navigation to intercept external links.
    /// </summary>
    private async void OnNotesWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        try
        {
            if (e.Url?.Contains("#height:") == true)
            {
                e.Cancel = true;
                var parts = e.Url.Split(["#height:"], StringSplitOptions.None);
                if (parts.Length > 1 && int.TryParse(parts[1], out var height))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        NotesWebView.HeightRequest = Math.Min(height, 800);
                    });
                }
                return;
            }

            if (e.Url?.StartsWith("http://") == true || e.Url?.StartsWith("https://") == true)
            {
                e.Cancel = true;
                try
                {
                    await Launcher.OpenAsync(new Uri(e.Url));
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Retrieve the edited HTML content from the modal WebView editor.
    /// </summary>
    private async Task<string> GetEditedNotesHtmlFromModalAsync()
    {
        try
        {
            var result = await NotesWebViewModal.EvaluateJavaScriptAsync("window.__editorGetContent()");

            if (string.IsNullOrEmpty(result))
            {
                return string.Empty;
            }

            string html;
            try
            {
                html = JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            }
            catch
            {
                if (result.StartsWith("\"") && result.EndsWith("\"") && result.Length > 1)
                {
                    html = result[1..^1];
                }
                else
                {
                    html = result;
                }
            }

            try
            {
                if (!string.IsNullOrEmpty(html) && (html.Contains("\\u003C", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("\\u003E", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("\\\"", StringComparison.Ordinal)))
                {
                    html = Regex.Unescape(html);
                }
            }
            catch { }

            return html;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void OnCloseNotesEditorModal(object sender, EventArgs e)
    {
        _ = CloseNotesEditorModalAsync(false);
    }

    private void OnModalBackdropTapped(object? sender, EventArgs e)
    {
        _ = CloseNotesEditorModalAsync(false);
    }

    private async void OnSaveNotesEdit(object sender, EventArgs e)
    {
        try
        {
            var newNotes = await GetEditedNotesHtmlFromModalAsync();

            // Treat visually empty content as empty string
            try
            {
                var plain = Regex.Replace(newNotes ?? string.Empty, "<[^>]+>", " ");
                if (string.IsNullOrWhiteSpace(plain))
                {
                    newNotes = string.Empty;
                }
            }
            catch { }

            // Convert proxy URLs back to original URLs for server storage
            if (!string.IsNullOrEmpty(newNotes))
            {
                newNotes = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(newNotes, EffectiveBackendUrl);
            }

            await CloseNotesEditorModalAsync(true);
            NotesSaved?.Invoke(this, newNotes ?? string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditor] Save error: {ex.Message}");
        }
    }

    private void OnCancelNotesEdit(object sender, EventArgs e)
    {
        _ = CloseNotesEditorModalAsync(false);
    }

    /// <summary>
    /// Close the notes editor modal with optional unsaved changes check.
    /// </summary>
    private async Task CloseNotesEditorModalAsync(bool saved)
    {
        try
        {
            if (!saved && _isNotesModalOpen)
            {
                var currentContent = await GetEditedNotesHtmlFromModalAsync();

                var normalizedOriginal = NormalizeHtml(_originalNotesHtml ?? string.Empty);
                var normalizedCurrent = NormalizeHtml(currentContent);

                if (!string.Equals(normalizedOriginal, normalizedCurrent, StringComparison.Ordinal))
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
            }

            _isNotesModalOpen = false;
            NotesEditorModalOverlay.IsVisible = false;
            NotesEditorModalOverlay.InputTransparent = true;
            _originalNotesHtml = null;

            if (!saved)
            {
                EditorClosed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotesEditor] Close error: {ex.Message}");
        }
    }

    /// <summary>
    /// Normalize HTML for comparison.
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

    #endregion
}
