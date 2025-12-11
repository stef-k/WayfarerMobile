using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Factory for creating SSE client instances.
/// Each subscription requires its own client since SSE connections are long-lived.
/// </summary>
public interface ISseClientFactory
{
    /// <summary>
    /// Creates a new SSE client instance.
    /// </summary>
    /// <returns>A new <see cref="ISseClient"/> instance.</returns>
    ISseClient Create();
}

/// <summary>
/// Factory implementation for creating SSE client instances.
/// </summary>
public class SseClientFactory : ISseClientFactory
{
    private readonly ISettingsService _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="SseClientFactory"/>.
    /// </summary>
    /// <param name="settings">Settings service for server URL and API token.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating clients.</param>
    public SseClientFactory(
        ISettingsService settings,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc />
    public ISseClient Create()
    {
        var logger = _loggerFactory.CreateLogger<SseClient>();
        return new SseClient(_settings, logger, _httpClientFactory);
    }
}
