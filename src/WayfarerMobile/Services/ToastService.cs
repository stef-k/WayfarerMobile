using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Toast notification service using simple alerts as fallback.
/// For full toast functionality, use ToastNotification control directly on pages.
/// </summary>
public class ToastService : IToastService
{
    /// <summary>
    /// Shows a toast notification (falls back to a short alert).
    /// </summary>
    public async Task ShowAsync(string message, int duration = 3000)
    {
        await ShowAlertAsync("", message);
    }

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    public async Task ShowSuccessAsync(string message)
    {
        await ShowAlertAsync("✓ Success", message);
    }

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    public async Task ShowErrorAsync(string message)
    {
        await ShowAlertAsync("❌ Error", message);
    }

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    public async Task ShowWarningAsync(string message)
    {
        await ShowAlertAsync("⚠ Warning", message);
    }

    private static async Task ShowAlertAsync(string title, string message)
    {
        var page = GetCurrentPage();
        if (page != null)
        {
            if (string.IsNullOrEmpty(title))
            {
                // Short info toast - just show message
                await page.DisplayAlertAsync("", message, "OK");
            }
            else
            {
                await page.DisplayAlertAsync(title, message, "OK");
            }
        }
    }

    private static Page? GetCurrentPage()
    {
        if (Application.Current?.Windows.Count > 0)
        {
            return Application.Current.Windows[0].Page;
        }
        return null;
    }
}
