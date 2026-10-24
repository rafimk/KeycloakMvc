using KeycloakMvc.Authentication.Keycloak;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeycloakMvc.HealthChecks;

public static class HealthCheckExtensions
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHttpClient(KeycloakHealthCheck.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        var checks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("The application is running."), tags: [LiveTag]);
        foreach (var instance in KeycloakInstances.All)
        {
            checks.AddTypeActivatedCheck<KeycloakHealthCheck>(
                $"keycloak-{instance.Name.ToLowerInvariant()}", null, [ReadyTag], instance);
        }
        checks.AddCheck<ActiveDirectoryHealthCheck>("active-directory", tags: [ReadyTag]);
        return services;
    }

    /// <summary>
    /// /health: liveness (the process answers). /health/ready: each Keycloak instance and Active Directory.
    /// Degraded still returns HTTP 200; Unhealthy returns 503.
    /// </summary>
    public static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", Options(LiveTag)).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", Options(ReadyTag)).AllowAnonymous();
        return endpoints;
    }

    private static HealthCheckOptions Options(string tag) => new()
    {
        Predicate = registration => registration.Tags.Contains(tag),
        ResponseWriter = WriteJsonAsync,
    };

    // Exceptions are logged by the health check service, never returned to callers.
    private static Task WriteJsonAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds),
                data = entry.Value.Data.Count > 0 ? entry.Value.Data : null,
            }),
        });
}
