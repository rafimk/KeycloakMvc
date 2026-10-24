namespace KeycloakMvc.Models;

public class SelectAuthenticationViewModel
{
    public string ReturnUrl { get; set; } = "/";

    /// <summary>One button per Keycloak instance, in the order of KeycloakInstances.All.</summary>
    public IReadOnlyList<KeycloakSignInOption> KeycloakInstances { get; set; } = [];
}

/// <param name="Instance">Posted back to AccountController.LoginWithKeycloak.</param>
public record KeycloakSignInOption(string Instance, string DisplayName, string? Description);
