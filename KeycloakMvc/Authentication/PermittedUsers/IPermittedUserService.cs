using System.Security.Claims;

namespace KeycloakMvc.Authentication.PermittedUsers;

public interface IPermittedUserService
{
    /// <summary>True when the signed-in user is on the permitted users list for their identity provider.</summary>
    bool IsPermitted(ClaimsPrincipal user);
}
