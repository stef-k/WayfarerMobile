using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views.Controls;

/// <summary>
/// Content view for displaying trip overview and details.
/// This is embedded inside MainPage's SfBottomSheet.BottomSheetContent.
/// </summary>
public partial class TripOverviewContent : ContentView
{
    /// <summary>
    /// Creates a new instance of TripOverviewContent.
    /// </summary>
    public TripOverviewContent()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles WebView navigation to open external links in the system browser.
    /// </summary>
    private async void OnNotesWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Allow about:blank and data URIs for initial content loading
        if (e.Url.StartsWith("about:") || e.Url.StartsWith("data:") || e.Url.StartsWith("file:"))
            return;

        // Allow height notifications via hash
        if (e.Url.Contains("#height:"))
        {
            e.Cancel = true;
            return;
        }

        // For http/https links, open in system browser
        if (e.Url.StartsWith("http://") || e.Url.StartsWith("https://"))
        {
            e.Cancel = true;
            try
            {
                await Launcher.OpenAsync(new Uri(e.Url));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TripOverviewContent] Failed to open URL: {ex.Message}");
            }
        }
    }
}
