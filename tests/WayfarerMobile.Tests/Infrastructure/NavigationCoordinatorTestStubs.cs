using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

public sealed class NavigationHudViewModel : IDisposable
{
    public event EventHandler<string?>? StopNavigationRequested
    {
        add { }
        remove { }
    }

    public Task StartNavigationAsync(NavigationRoute route) => Task.CompletedTask;

    public void StopNavigationDisplay() { }

    public void Dispose() { }
}
