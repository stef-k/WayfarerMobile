public static class FileSystem
{
    public static string CacheDirectory => Path.GetTempPath();
}

public sealed class Share
{
    public static Share Default { get; } = new();

    public Task RequestAsync(ShareFileRequest request) => Task.CompletedTask;
    public Task RequestAsync(ShareTextRequest request) => Task.CompletedTask;
}

public sealed class ShareTextRequest
{
    public string? Text { get; init; }
    public string? Title { get; init; }
}

public sealed class ShareFileRequest
{
    public string? Title { get; init; }
    public ShareFile? File { get; init; }
}

public sealed class ShareFile(string fullPath)
{
    public string FullPath { get; } = fullPath;
}

public sealed record Location(double Latitude, double Longitude);

public sealed class MapLaunchOptions
{
    public string? Name { get; init; }
}

public sealed class Map
{
    public static Map Default { get; } = new();

    public Task OpenAsync(Location location, MapLaunchOptions options) => Task.CompletedTask;
}

public sealed class Clipboard
{
    public static Clipboard Default { get; } = new();

    public Task SetTextAsync(string text) => Task.CompletedTask;
}

public sealed class Launcher
{
    public static Launcher Default { get; } = new();

    public Task<bool> OpenAsync(Uri uri) => Task.FromResult(true);
}

public enum NetworkAccess { None, Internet }
public sealed class ConnectivityChangedEventArgs(NetworkAccess networkAccess) : EventArgs { public NetworkAccess NetworkAccess { get; } = networkAccess; }
public interface IConnectivity
{
    NetworkAccess NetworkAccess { get; }
    event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
}
public static class Connectivity { public static IConnectivity Current { get; set; } = new ConnectivityStub(); private sealed class ConnectivityStub : IConnectivity { public NetworkAccess NetworkAccess => NetworkAccess.Internet; public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged; } }
namespace Microsoft.Maui.ApplicationModel
{
    public static class MainThread
    {
        public static void BeginInvokeOnMainThread(Action action) => action();

        public static Task InvokeOnMainThreadAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}

namespace Microsoft.Maui.ApplicationModel.DataTransfer
{
}
