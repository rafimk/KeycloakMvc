namespace KeycloakMvc.Authentication;

/// <summary>
/// Claim types shared by both sign-in methods. The OIDC handler keeps Keycloak's JWT claim
/// names (MapInboundClaims = false), so Active Directory sign-ins use the same names.
/// </summary>
public static class AppClaimTypes
{
    public const string UserName = "preferred_username";
    public const string DisplayName = "name";
    public const string Role = "role";
    /// <summary>Provider-qualified identifier, unique across domains and identity providers.</summary>
    public const string UserId = "app_user_id";
    public const string AuthenticationMethod = "app_auth_method";
    /// <summary>The AD domain, or the Keycloak issuer URL.</summary>
    public const string AuthenticationDomain = "app_auth_domain";
    /// <summary>The Keycloak instance name (Primary or Secondary). Not set for Active Directory.</summary>
    public const string KeycloakInstance = "app_keycloak_instance";
}

public static class AppAuthenticationMethods
{
    /// <summary>Keycloak sign-in where the ID token's amr claim confirms Kerberos.</summary>
    public const string KeycloakKerberos = "KeycloakKerberos";
    /// <summary>Keycloak sign-in whose upstream method could not be verified.</summary>
    public const string KeycloakSso = "KeycloakSso";
    public const string ActiveDirectory = "ActiveDirectory";
}
