namespace KeycloakMvc.Models;

public class SelectAuthenticationViewModel
{
    public string ReturnUrl { get; set; } = "/";

    public bool PasswordLoginEnabled { get; set; }
}
