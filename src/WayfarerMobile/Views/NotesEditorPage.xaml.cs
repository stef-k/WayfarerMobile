using System.Text.Json;
using System.Text.RegularExpressions;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for editing location notes using Quill.js rich text editor.
/// </summary>
public partial class NotesEditorPage : ContentPage
{
    private readonly NotesEditorViewModel _viewModel;
    private bool _isEditorLoaded;

    /// <summary>
    /// Creates a new instance of NotesEditorPage.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    public NotesEditorPage(NotesEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        await LoadEditorAsync();
    }

    private async Task LoadEditorAsync()
    {
        try
        {
            var html = await _viewModel.GetEditorHtmlAsync();
            if (!string.IsNullOrEmpty(html))
            {
                EditorWebView.Source = new HtmlWebViewSource { Html = html };
                _isEditorLoaded = true;

                // Set up content change tracking after editor loads
                await Task.Delay(1000); // Wait for Quill to initialize
                StartContentChangeTracking();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotesEditorPage] Failed to load editor: {ex.Message}");
        }
    }

    private void StartContentChangeTracking()
    {
        // Poll for content changes every second
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!_isEditorLoaded) return false;

            _ = UpdateContentAsync();
            return true; // Continue timer
        });
    }

    private async Task UpdateContentAsync()
    {
        try
        {
            var result = await EditorWebView.EvaluateJavaScriptAsync("window.__editorGetContent()");

            if (!string.IsNullOrEmpty(result))
            {
                var html = ParseJsResult(result);
                _viewModel.SetCurrentContent(html);
            }
        }
        catch
        {
            // Ignore errors during content polling
        }
    }

    private static string ParseJsResult(string result)
    {
        try
        {
            var html = JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            return UnescapeHtml(html);
        }
        catch
        {
            if (result.StartsWith("\"") && result.EndsWith("\"") && result.Length > 1)
            {
                return UnescapeHtml(result[1..^1]);
            }
            return result;
        }
    }

    private static string UnescapeHtml(string html)
    {
        try
        {
            if (!string.IsNullOrEmpty(html) && (html.Contains("\\u003C", StringComparison.OrdinalIgnoreCase)
                || html.Contains("\\u003E", StringComparison.OrdinalIgnoreCase)
                || html.Contains("\\\"", StringComparison.Ordinal)))
            {
                return Regex.Unescape(html);
            }
        }
        catch { }

        return html;
    }

    /// <summary>
    /// Gets the current editor content before saving.
    /// </summary>
    public async Task<string> GetCurrentContentAsync()
    {
        try
        {
            var result = await EditorWebView.EvaluateJavaScriptAsync("window.__editorGetContent()");
            return ParseJsResult(result ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isEditorLoaded = false;
    }

    /// <summary>
    /// Handles Save button click - ensures latest content is captured before saving.
    /// </summary>
    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            // Get the latest content from the WebView before saving
            var content = await GetCurrentContentAsync();
            _viewModel.SetCurrentContent(content);

            // Execute the ViewModel's save command
            if (_viewModel.SaveCommand.CanExecute(null))
            {
                await _viewModel.SaveCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotesEditorPage] Save error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles WebView navigation to open external links in browser.
    /// </summary>
    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        try
        {
            // Open external links in browser
            if (e.Url?.StartsWith("http://") == true || e.Url?.StartsWith("https://") == true)
            {
                e.Cancel = true;
                await Launcher.OpenAsync(new Uri(e.Url));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotesEditorPage] Failed to open link: {ex.Message}");
        }
    }
}
