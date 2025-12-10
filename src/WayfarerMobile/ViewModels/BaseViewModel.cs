using CommunityToolkit.Mvvm.ComponentModel;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Base class for all ViewModels providing common functionality.
/// Uses CommunityToolkit.Mvvm source generators.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets whether the ViewModel is busy performing an operation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>
    /// Gets or sets the title for the view.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets whether the ViewModel is not busy.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public virtual Task OnAppearingAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public virtual Task OnDisappearingAsync()
    {
        return Task.CompletedTask;
    }
}
