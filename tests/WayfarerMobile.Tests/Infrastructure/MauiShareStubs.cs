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
