using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.Authentication.PermittedUsers;

/// <summary>Checks users against <see cref="PermittedUsersOptions"/>. Changes to appsettings apply without a restart.</summary>
public sealed class PermittedUserService(IOptionsMonitor<PermittedUsersOptions> options) : IPermittedUserService
{
    public bool IsPermitted(ClaimsPrincipal user)
    {
        var userName = user.FindFirstValue(AppClaimTypes.UserName);
        if (string.IsNullOrEmpty(userName))
            return false;

        var permitted = options.CurrentValue;
        if (user.FindFirstValue(AppClaimTypes.AuthenticationMethod) == AppAuthenticationMethods.ActiveDirectory)
        {
            var domain = user.FindFirstValue(AppClaimTypes.AuthenticationDomain);
            return !string.IsNullOrEmpty(domain) &&
                permitted.ActiveDirectory.Contains($"{userName}@{domain}", StringComparer.OrdinalIgnoreCase);
        }
        var instance = user.FindFirstValue(AppClaimTypes.KeycloakInstance);
        return permitted.Keycloak
            .Where(pair => string.Equals(pair.Key, instance, StringComparison.OrdinalIgnoreCase))
            .Any(pair => pair.Value.Contains(userName, StringComparer.OrdinalIgnoreCase));
    }
}
