namespace KeycloakMvc.Authentication.ActiveDirectory;

public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    public List<ActiveDirectoryDomainOptions> Domains { get; set; } = [];
}

public sealed class ActiveDirectoryDomainOptions
{
    /// <summary>Label shown in the domain list on the login page.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>DNS name of the domain, for example corp.example.com.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Optional NetBIOS name, so users can type CORP\user.</summary>
    public string? NetBiosName { get; set; }

    /// <summary>Domain controller host name. It must match the LDAPS certificate.</summary>
    public string Server { get; set; } = string.Empty;

    public int Port { get; set; } = 636;

    public bool UseSsl { get; set; } = true;

    /// <summary>Where to look up users. Defaults to the domain root (DC=corp,DC=example,DC=com).</summary>
    public string? SearchBase { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Application roles granted to members of specific AD groups. No mapping, no role.</summary>
    public List<ActiveDirectoryRoleMapping> RoleMappings { get; set; } = [];
}

public sealed class ActiveDirectoryRoleMapping
{
    /// <summary>Distinguished name of the group, as it appears in the user's memberOf attribute.</summary>
    public string Group { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}
