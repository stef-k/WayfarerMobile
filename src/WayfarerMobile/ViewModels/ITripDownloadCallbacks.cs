namespace WayfarerMobile.ViewModels;

public interface ITripDownloadCallbacks
{
    Task RefreshTripsAsync();
    void UpdateItemProgress(Guid serverId, double progress, bool isDownloading);
}
