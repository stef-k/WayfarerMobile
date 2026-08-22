public static class FileSystem
{
    public static string CacheDirectory => Path.GetTempPath();
}

public sealed class Share
{
    public static Share Default { get; } = new();

    public Task RequestAsync(ShareFileRequest request) => Task.CompletedTask;
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

public enum NetworkAccess { None, Internet }
public sealed class ConnectivityChangedEventArgs(NetworkAccess networkAccess) : EventArgs { public NetworkAccess NetworkAccess { get; } = networkAccess; }
public interface IConnectivity
{
    NetworkAccess NetworkAccess { get; }
    event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
}
public static class Connectivity { public static IConnectivity Current { get; set; } = new ConnectivityStub(); private sealed class ConnectivityStub : IConnectivity { public NetworkAccess NetworkAccess => NetworkAccess.Internet; public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged; } }
public static class MainThread { public static void BeginInvokeOnMainThread(Action action) => action(); }
