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
        return permitted.Keycloak.Contains(userName, StringComparer.OrdinalIgnoreCase);
    }
}
