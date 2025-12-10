using CommunityToolkit.Mvvm.Input;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the About page.
/// </summary>
public partial class AboutViewModel : BaseViewModel
{
    /// <summary>
    /// Gets the app version string.
    /// </summary>
    public string AppVersion => $"Version {AppInfo.VersionString} ({AppInfo.BuildString})";

    /// <summary>
    /// Creates a new instance of AboutViewModel.
    /// </summary>
    public AboutViewModel()
    {
        Title = "About";
    }

    /// <summary>
    /// Closes the about page.
    /// </summary>
    [RelayCommand]
    private async Task CloseAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
