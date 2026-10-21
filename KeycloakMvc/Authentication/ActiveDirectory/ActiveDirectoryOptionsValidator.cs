using Microsoft.Extensions.Options;

namespace KeycloakMvc.Authentication.ActiveDirectory;

/// <summary>Rejects unsafe or incomplete Active Directory settings at startup.</summary>
public sealed class ActiveDirectoryOptionsValidator : IValidateOptions<ActiveDirectoryOptions>
{
    public ValidateOptionsResult Validate(string? name, ActiveDirectoryOptions options)
    {
        var errors = new List<string>();
        for (var i = 0; i < options.Domains.Count; i++)
        {
            var domain = options.Domains[i];
            var path = $"{ActiveDirectoryOptions.SectionName}:Domains:{i}";
            if (string.IsNullOrWhiteSpace(domain.Name))
                errors.Add($"{path}:Name is required.");
            if (Uri.CheckHostName(domain.Domain) != UriHostNameType.Dns || !domain.Domain.Contains('.'))
                errors.Add($"{path}:Domain must be a DNS domain name such as corp.example.com.");
            // A host name (not an IP address) is needed for LDAPS certificate validation.
            if (Uri.CheckHostName(domain.Server) != UriHostNameType.Dns)
                errors.Add($"{path}:Server must be the domain controller's DNS host name.");
            if (domain.Port is < 1 or > 65535)
                errors.Add($"{path}:Port must be between 1 and 65535.");
            if (!domain.UseSsl)
                errors.Add($"{path}:UseSsl must be true. Passwords are never sent over unencrypted LDAP.");
            if (domain.TimeoutSeconds is < 1 or > 60)
                errors.Add($"{path}:TimeoutSeconds must be between 1 and 60.");
            if (domain.RoleMappings.Any(m => string.IsNullOrWhiteSpace(m.Group) || string.IsNullOrWhiteSpace(m.Role)))
                errors.Add($"{path}:RoleMappings entries need both Group and Role.");
        }

        AddDuplicateErrors(errors, options.Domains.Select(d => d.Name), "Name");
        AddDuplicateErrors(errors, options.Domains.Select(d => d.Domain), "Domain");
        AddDuplicateErrors(errors, options.Domains.Select(d => d.NetBiosName).OfType<string>(), "NetBiosName");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void AddDuplicateErrors(List<string> errors, IEnumerable<string> values, string property)
    {
        foreach (var duplicate in values.Where(v => !string.IsNullOrWhiteSpace(v))
                     .GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"{ActiveDirectoryOptions.SectionName}:Domains {property} '{duplicate.Key}' is configured more than once.");
        }
    }
}
