using Microsoft.Extensions.Options;

namespace KeycloakMvc.Authentication.PermittedUsers;

public sealed class PermittedUsersOptionsValidator : IValidateOptions<PermittedUsersOptions>
{
    public ValidateOptionsResult Validate(string? name, PermittedUsersOptions options)
    {
        var errors = new List<string>();
        if (options.Keycloak.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{PermittedUsersOptions.SectionName}:Keycloak must not contain empty entries.");
        foreach (var entry in options.ActiveDirectory)
        {
            var parts = entry?.Split('@') ?? [];
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !parts[1].Contains('.'))
                errors.Add($"{PermittedUsersOptions.SectionName}:ActiveDirectory entry '{entry}' must be username@dns.domain.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
