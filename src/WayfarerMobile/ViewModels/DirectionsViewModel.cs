using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>Presentation state for one coordinator-owned Directions invocation.</summary>
public partial class DirectionsViewModel : ObservableObject
{
    private readonly Func<DirectionsAction, HostedProviderMode?, Task> submit;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DirectionsViewModel(Func<DirectionsAction, HostedProviderMode?, Task> submit,
        bool segmentContext, bool externalMaps, bool retained)
    {
        this.submit = submit;
        HasSegmentContext = segmentContext;
        HasExternalMaps = externalMaps;
        HasRetainedRoute = retained;
    }

    public bool HasSegmentContext { get; }
    public bool HasExternalMaps { get; }
    public bool HasRetainedRoute { get; }
    public Task Completion => completion.Task;
    public bool IsComplete => completion.Task.IsCompleted;
    public string LoadLabel => HasRetainedRoute ? "Refresh with Wayfarer" : "Show route options";
    [ObservableProperty] private string _status = string.Empty;
    public bool CanChoose => !IsBusy && !IsComplete;
    public bool CanChooseDirect => !IsCalculating && !IsComplete;
    public bool HasModes => Modes.Count > 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChoose))] private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChooseDirect))] private bool _isCalculating;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasModes))]
    private IReadOnlyList<HostedProviderMode> _modes = [];

    public void Complete()
    {
        if (!completion.TrySetResult()) return;
        OnPropertyChanged(nameof(CanChoose));
        OnPropertyChanged(nameof(CanChooseDirect));
    }

    [RelayCommand]
    private Task LoadAsync() => SubmitAsync(DirectionsAction.Load);
    [RelayCommand]
    private Task RetryAsync() => SubmitAsync(DirectionsAction.Retry);
    [RelayCommand]
    private Task ChooseModeAsync(HostedProviderMode mode) => SubmitAsync(DirectionsAction.Mode, mode);
    [RelayCommand]
    private Task DirectAsync() => SubmitAsync(DirectionsAction.Direct);
    [RelayCommand]
    private Task UseRetainedAsync() => SubmitAsync(DirectionsAction.Retained);
    [RelayCommand]
    private Task ExternalMapsAsync() => SubmitAsync(DirectionsAction.ExternalMaps);
    [RelayCommand]
    private Task CancelAsync() => SubmitAsync(DirectionsAction.Cancel);

    private Task SubmitAsync(DirectionsAction action, HostedProviderMode? mode = null)
    {
        if (IsComplete) return Task.CompletedTask;
        if (action == DirectionsAction.Cancel) return submit(action, null);
        if (action == DirectionsAction.Direct && !IsCalculating) return submit(action, null);
        if (IsBusy || (action == DirectionsAction.Retry && !CanRetry)) return Task.CompletedTask;
        return submit(action, mode);
    }
}

public enum DirectionsAction { Load, Mode, Retry, Direct, Cancel, Retained, ExternalMaps }
