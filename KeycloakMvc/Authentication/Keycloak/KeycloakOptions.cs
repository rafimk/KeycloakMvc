using System.ComponentModel.DataAnnotations;

namespace KeycloakMvc.Authentication.Keycloak;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm URL, for example https://sso.example.com/realms/mvc-sample. Also the expected token issuer.</summary>
    [Required, Url]
    public string Authority { get; set; } = string.Empty;

    /// <summary>Optional discovery document URL, when the app reaches Keycloak by a different address.</summary>
    [Url]
    public string? MetadataAddress { get; set; }

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Optional amr value that a Keycloak AMR mapper emits for the Kerberos execution. Without it the
    /// app cannot prove Kerberos was used and reports plain Keycloak SSO.
    /// </summary>
    public string? KerberosAmrValue { get; set; }

    public string DiscoveryDocumentUrl => MetadataAddress ?? $"{Authority}/.well-known/openid-configuration";
}
