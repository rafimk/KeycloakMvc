using KeycloakMvc.Authentication.Keycloak;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.HealthChecks;

/// <summary>Checks that Keycloak's discovery document can be downloaded.</summary>
public sealed class KeycloakHealthCheck(HttpClient httpClient, IOptions<KeycloakOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(options.Value.DiscoveryDocumentUrl, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Keycloak is reachable.")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"Keycloak returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Keycloak is unreachable.", ex);
        }
    }
}
