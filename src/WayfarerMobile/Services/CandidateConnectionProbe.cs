using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

public interface ICandidateConnectionProbe
{
    Task<bool> TestAsync(string serverUrl, string apiToken, CancellationToken cancellationToken);
}

public sealed class CandidateConnectionProbe : ICandidateConnectionProbe
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<CandidateConnectionProbe> logger;

    public CandidateConnectionProbe(IHttpClientFactory httpClientFactory,
        ILogger<CandidateConnectionProbe> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public async Task<bool> TestAsync(string serverUrl, string apiToken,
        CancellationToken cancellationToken)
    {
        var normalizedServer = HostedRouteServerIdentity.Normalize(serverUrl);
        if (normalizedServer.Length == 0 || string.IsNullOrWhiteSpace(apiToken)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{normalizedServer.TrimEnd('/')}/api/settings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClientFactory.CreateClient("WayfarerApi")
                .SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Candidate connection probe failed: {FailureType}",
                exception.GetType().Name);
            return false;
        }
    }
}
