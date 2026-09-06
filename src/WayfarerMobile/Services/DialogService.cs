using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for displaying user-friendly dialogs using MAUI Shell.
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    public async Task ShowErrorAsync(string title, string message)
    {
        var page = GetCurrentPage();
        if (page != null)
        {
            await page.DisplayAlertAsync($"❌ {title}", message, "OK");
        }
    }

    /// <summary>
    /// Shows a success dialog.
    /// </summary>
    public async Task ShowSuccessAsync(string title, string message)
    {
        var page = GetCurrentPage();
        if (page != null)
        {
            await page.DisplayAlertAsync($"✓ {title}", message, "OK");
        }
    }

    /// <summary>
    /// Shows an info dialog.
    /// </summary>
    public async Task ShowInfoAsync(string title, string message)
    {
        var page = GetCurrentPage();
        if (page != null)
        {
            await page.DisplayAlertAsync($"ℹ️ {title}", message, "OK");
        }
    }

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    public async Task<bool> ShowConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        var page = GetCurrentPage();
        if (page != null)
        {
            return await page.DisplayAlertAsync(title, message, accept, cancel);
        }
        return false;
    }

    /// <summary>
    /// Shows an error dialog with optional retry action.
    /// </summary>
    public async Task ShowErrorWithRetryAsync(string title, string message, Func<Task>? retryAction = null)
    {
        var page = GetCurrentPage();
        if (page == null) return;

        if (retryAction != null)
        {
            var retry = await page.DisplayAlertAsync($"❌ {title}", message, "Retry", "Cancel");
            if (retry)
            {
                await retryAction();
            }
        }
        else
        {
            await page.DisplayAlertAsync($"❌ {title}", message, "OK");
        }
    }

    /// <inheritdoc />
    public async Task<string?> SelectAsync(string title, IReadOnlyList<string> choices, string cancel = "Cancel")
    {
        var page = GetCurrentPage();
        if (page == null) throw new InvalidOperationException("Navigation selection is unavailable. Reopen the map and try again.");
        var selected = await page.DisplayActionSheetAsync(title, cancel, null, choices.ToArray());
        return selected == cancel || !choices.Contains(selected) ? null : selected;
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
