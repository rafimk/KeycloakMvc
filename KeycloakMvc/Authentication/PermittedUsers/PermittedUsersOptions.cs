namespace KeycloakMvc.Authentication.PermittedUsers;

/// <summary>
/// Users allowed to use the application after they sign in. Lists are per identity provider,
/// so the same name in Keycloak and in Active Directory is never treated as the same user.
/// Empty lists permit nobody.
/// </summary>
public sealed class PermittedUsersOptions
{
    public const string SectionName = "PermittedUsers";

    /// <summary>Keycloak usernames (the preferred_username claim), for example "demo".</summary>
    public List<string> Keycloak { get; set; } = [];

    /// <summary>Active Directory users as username@dns.domain, for example "alice@lab.test".</summary>
    public List<string> ActiveDirectory { get; set; } = [];
}
