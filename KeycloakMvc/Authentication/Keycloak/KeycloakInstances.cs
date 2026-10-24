namespace KeycloakMvc.Authentication.Keycloak;

/// <summary>
/// A Keycloak instance offered on the sign-in page. Each one is its own OIDC scheme, with its own
/// callback paths, settings section (Keycloak:{Name}) and permitted users list.
/// </summary>
public sealed record KeycloakInstance(
    string Name, string Scheme, string CallbackPath, string SignedOutCallbackPath, string RemoteSignOutPath)
{
    public string SectionName => $"{KeycloakOptions.SectionName}:{Name}";
}

public static class KeycloakInstances
{
    // Primary keeps the OIDC handler's default paths, which the existing realm already allows.
    public static readonly KeycloakInstance Primary = new(
        "Primary", "KeycloakPrimary", "/signin-oidc", "/signout-callback-oidc", "/signout-oidc");

    public static readonly KeycloakInstance Secondary = new(
        "Secondary", "KeycloakSecondary", "/signin-oidc-secondary", "/signout-callback-oidc-secondary", "/signout-oidc-secondary");

    /// <summary>In the order the sign-in page shows them.</summary>
    public static IReadOnlyList<KeycloakInstance> All { get; } = [Primary, Secondary];

    public static KeycloakInstance? Find(string? name) =>
        All.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    public static KeycloakInstance FindByScheme(string scheme) => All.First(i => i.Scheme == scheme);
}
