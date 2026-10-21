namespace KeycloakMvc.Models;

public class HomeViewModel
{
    public string UserName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string AuthenticationMethod { get; set; } = string.Empty;

    public bool IsActiveDirectory { get; set; }

    /// <summary>The AD domain, or the Keycloak issuer URL.</summary>
    public string? Domain { get; set; }
}
