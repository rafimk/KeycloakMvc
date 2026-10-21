namespace KeycloakMvc.Authentication;

public static class AuthenticationConstants
{
    public const string SelectAuthenticationPath = "/Account/SelectAuthentication";
    public const string PasswordLoginPath = "/Account/Login";
    public const string LoginFailedPath = "/Account/LoginFailed";
    public const string NotAuthorizedPath = "/Account/NotAuthorized";
    public const string AccessDeniedPath = "/Account/AccessDenied";

    /// <summary>Rate limiting policy applied to username/password sign-in attempts.</summary>
    public const string PasswordLoginRateLimitPolicy = "password-login";
}
