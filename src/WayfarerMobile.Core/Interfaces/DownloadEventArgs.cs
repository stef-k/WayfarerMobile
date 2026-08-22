namespace WayfarerMobile.Core.Interfaces;

/// <summary>Progress for provider-independent Trip data transfer.</summary>
public sealed record DownloadProgressEventArgs
{
    public int TripId { get; init; }
    public int ProgressPercent { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}
