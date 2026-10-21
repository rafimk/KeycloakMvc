using KeycloakMvc.Authentication.PermittedUsers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace KeycloakMvc.Authentication;

/// <summary>
/// Re-checks the permitted users list on each request, so removing a user from appsettings ends
/// their session without waiting for it to expire.
/// </summary>
public sealed class AppCookieEvents(IPermittedUserService permittedUsers, ILogger<AppCookieEvents> logger)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is { } principal && !permittedUsers.IsPermitted(principal))
        {
            logger.LogWarning("Session for {UserName} ended: no longer a permitted user", principal.Identity?.Name);
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
