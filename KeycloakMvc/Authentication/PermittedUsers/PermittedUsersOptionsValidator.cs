using KeycloakMvc.Authentication.Keycloak;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.Authentication.PermittedUsers;

public sealed class PermittedUsersOptionsValidator : IValidateOptions<PermittedUsersOptions>
{
    public ValidateOptionsResult Validate(string? name, PermittedUsersOptions options)
    {
        var errors = new List<string>();
        foreach (var (instance, users) in options.Keycloak)
        {
            var path = $"{PermittedUsersOptions.SectionName}:Keycloak:{instance}";
            if (KeycloakInstances.Find(instance) is null)
                errors.Add($"{path} is not a Keycloak instance. Use {string.Join(" or ", KeycloakInstances.All.Select(i => i.Name))}.");
            if (users.Any(string.IsNullOrWhiteSpace))
                errors.Add($"{path} must not contain empty entries.");
        }
        foreach (var entry in options.ActiveDirectory)
        {
            var parts = entry?.Split('@') ?? [];
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !parts[1].Contains('.'))
                errors.Add($"{PermittedUsersOptions.SectionName}:ActiveDirectory entry '{entry}' must be username@dns.domain.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
