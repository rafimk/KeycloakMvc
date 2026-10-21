namespace KeycloakMvc.Authentication.ActiveDirectory;

public interface IActiveDirectoryService
{
    Task<ActiveDirectoryAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        string domain,
        CancellationToken cancellationToken = default);
}
