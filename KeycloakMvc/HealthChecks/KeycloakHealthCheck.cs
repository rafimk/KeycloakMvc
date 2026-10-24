using KeycloakMvc.Authentication.Keycloak;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.HealthChecks;

/// <summary>Checks that one Keycloak instance's discovery document can be downloaded.</summary>
public sealed class KeycloakHealthCheck(
    IHttpClientFactory httpClientFactory, IOptionsMonitor<KeycloakOptions> options, KeycloakInstance instance) : IHealthCheck
{
    public const string HttpClientName = "keycloak-health";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            var discoveryDocumentUrl = options.Get(instance.Scheme).DiscoveryDocumentUrl;
            using var response = await httpClient.GetAsync(discoveryDocumentUrl, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"Keycloak {instance.Name} is reachable.")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"Keycloak {instance.Name} returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Keycloak {instance.Name} is unreachable.", ex);
        }
    }
}
