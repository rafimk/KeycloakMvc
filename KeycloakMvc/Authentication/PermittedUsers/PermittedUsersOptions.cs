namespace KeycloakMvc.Authentication.PermittedUsers;

/// <summary>
/// Users allowed to use the application after they sign in. Lists are per identity provider and
/// per Keycloak instance, so the same name in two providers is never treated as the same user.
/// Empty lists permit nobody.
/// </summary>
public sealed class PermittedUsersOptions
{
    public const string SectionName = "PermittedUsers";

    /// <summary>
    /// Keycloak usernames (the preferred_username claim) per instance name, for example
    /// "Primary": [ "demo" ].
    /// </summary>
    public Dictionary<string, List<string>> Keycloak { get; set; } = new();

    /// <summary>Active Directory users as username@dns.domain, for example "alice@lab.test".</summary>
    public List<string> ActiveDirectory { get; set; } = [];
}
