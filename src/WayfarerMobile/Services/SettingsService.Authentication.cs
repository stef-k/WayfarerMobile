namespace WayfarerMobile.Services;

public partial class SettingsService
{
    private readonly CommittedAuthenticationAuthority authenticationAuthority;

    /// <inheritdoc/>
    public string? ServerUrl => authenticationAuthority.Current.ServerUrl;

    /// <inheritdoc/>
    public string? ApiToken => authenticationAuthority.Current.ApiToken;

    /// <inheritdoc/>
    public Guid RoutingAccountPartition => authenticationAuthority.Current.RoutingPartition;

    /// <inheritdoc/>
    public long AuthenticationSessionRevision => authenticationAuthority.Revision;

    /// <inheritdoc/>
    public Task PreloadSecureSettingsAsync() => authenticationAuthority.PreloadAsync();

    /// <inheritdoc/>
    public Task CommitAuthenticationAsync(string serverUrl, string apiToken,
        CancellationToken cancellationToken = default) =>
        authenticationAuthority.CommitAsync(serverUrl, apiToken, cancellationToken);

    /// <inheritdoc/>
    public Task ClearAuthenticationAsync(CancellationToken cancellationToken = default) =>
        authenticationAuthority.ClearAsync(cancellationToken);
}
