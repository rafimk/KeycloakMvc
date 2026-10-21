using System.DirectoryServices.Protocols;
using KeycloakMvc.Authentication.ActiveDirectory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.HealthChecks;

/// <summary>
/// Reads each configured domain controller's rootDSE anonymously over LDAPS, which checks the
/// connection and the server certificate without any credentials. An unreachable domain only
/// degrades the application, because the other sign-in methods still work.
/// </summary>
public sealed class ActiveDirectoryHealthCheck(IOptions<ActiveDirectoryOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var domains = options.Value.Domains;
        if (domains.Count == 0)
            return HealthCheckResult.Healthy("No Active Directory domains are configured.");

        var reachable = await Task.WhenAll(domains.Select(d => IsReachableAsync(d, cancellationToken)));
        var data = domains.Zip(reachable).ToDictionary(
            pair => pair.First.Domain, pair => (object)(pair.Second ? "reachable" : "unreachable"));
        var unreachable = domains.Where((_, i) => !reachable[i]).Select(d => d.Domain).ToList();
        return unreachable.Count == 0
            ? HealthCheckResult.Healthy("All Active Directory domains are reachable.", data)
            : HealthCheckResult.Degraded($"Unreachable: {string.Join(", ", unreachable)}.", data: data);
    }

    private static async Task<bool> IsReachableAsync(ActiveDirectoryDomainOptions domain, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = ActiveDirectoryConnectionFactory.Create(domain, AuthType.Anonymous);
            var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext");
            await ActiveDirectoryConnectionFactory.SearchAsync(connection, request, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
