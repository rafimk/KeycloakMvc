using System.Security.Claims;
using KeycloakMvc.Authentication.ActiveDirectory;

namespace KeycloakMvc.Authentication;

public static class AppPrincipalFactory
{
    /// <summary>Builds the session principal for a user verified by Active Directory. Never includes the password.</summary>
    public static ClaimsPrincipal CreateActiveDirectoryPrincipal(ActiveDirectoryUser user)
    {
        List<Claim> claims =
        [
            new(AppClaimTypes.UserId, $"ad|{user.Domain}|{user.ObjectGuid}"),
            new(AppClaimTypes.UserName, user.UserName),
            new(AppClaimTypes.AuthenticationMethod, AppAuthenticationMethods.ActiveDirectory),
            new(AppClaimTypes.AuthenticationDomain, user.Domain),
        ];
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            claims.Add(new(AppClaimTypes.DisplayName, user.DisplayName));
        claims.AddRange(user.Roles.Select(role => new Claim(AppClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims, AppAuthenticationMethods.ActiveDirectory, AppClaimTypes.UserName, AppClaimTypes.Role));
    }
}
