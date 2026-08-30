using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

public partial class SettingsService
{
    // Cached values avoid blocking SecureStorage calls on the main thread.
    private string? _cachedServerUrl;
    private string? _cachedApiToken;
    private bool _serverUrlLoaded;
    private bool _apiTokenLoaded;
    private long _authenticationSessionRevision;

    /// <inheritdoc/>
    public long AuthenticationSessionRevision => Interlocked.Read(ref _authenticationSessionRevision);

    /// <summary>
    /// Pre-loads secure settings from SecureStorage into memory cache.
    /// Call this at app startup to avoid blocking on first access.
    /// </summary>
    public async Task PreloadSecureSettingsAsync()
    {
        if (!_serverUrlLoaded)
        {
            try
            {
                SetCachedServerUrl(await SecureStorage.Default.GetAsync(KeyServerUrl));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "SecureStorage unavailable for ServerUrl");
                SetCachedServerUrl(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error loading ServerUrl");
                SetCachedServerUrl(null);
            }
            _serverUrlLoaded = true;
        }
        if (!_apiTokenLoaded)
        {
            try
            {
                SetCachedApiToken(await SecureStorage.Default.GetAsync(KeyApiToken));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "SecureStorage unavailable for ApiToken");
                SetCachedApiToken(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error loading ApiToken");
                SetCachedApiToken(null);
            }
            _apiTokenLoaded = true;
        }
    }

    /// <summary>Gets or sets the server URL used for API calls.</summary>
    public string? ServerUrl
    {
        get
        {
            if (!_serverUrlLoaded)
            {
                try
                {
                    SetCachedServerUrl(Task.Run(async () =>
                        await SecureStorage.Default.GetAsync(KeyServerUrl)).Result);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "SecureStorage unavailable for ServerUrl (sync)");
                    SetCachedServerUrl(null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error loading ServerUrl (sync)");
                    SetCachedServerUrl(null);
                }
                _serverUrlLoaded = true;
            }
            return _cachedServerUrl;
        }
        set
        {
            SetCachedServerUrl(value);
            _serverUrlLoaded = true;
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyServerUrl);
            }
            else
            {
                Task.Run(async () => await SecureStorage.Default.SetAsync(KeyServerUrl, value));
            }
        }
    }

    /// <summary>Gets or sets the API authentication token.</summary>
    public string? ApiToken
    {
        get
        {
            if (!_apiTokenLoaded)
            {
                try
                {
                    SetCachedApiToken(Task.Run(async () =>
                        await SecureStorage.Default.GetAsync(KeyApiToken)).Result);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "SecureStorage unavailable for ApiToken (sync)");
                    SetCachedApiToken(null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error loading ApiToken (sync)");
                    SetCachedApiToken(null);
                }
                _apiTokenLoaded = true;
            }
            return _cachedApiToken;
        }
        set
        {
            SetCachedApiToken(value);
            _apiTokenLoaded = true;
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyApiToken);
            }
            else
            {
                Task.Run(async () => await SecureStorage.Default.SetAsync(KeyApiToken, value));
            }
        }
    }

    private void SetCachedServerUrl(string? value)
    {
        if (string.Equals(_cachedServerUrl, value, StringComparison.Ordinal)) return;
        _cachedServerUrl = value;
        Interlocked.Increment(ref _authenticationSessionRevision);
    }

    private void SetCachedApiToken(string? value)
    {
        if (string.Equals(_cachedApiToken, value, StringComparison.Ordinal)) return;
        _cachedApiToken = value;
        Interlocked.Increment(ref _authenticationSessionRevision);
    }

    private void ClearCachedAuthentication()
    {
        if (_cachedServerUrl == null && _cachedApiToken == null) return;
        _cachedServerUrl = null;
        _cachedApiToken = null;
        Interlocked.Increment(ref _authenticationSessionRevision);
    }
}
