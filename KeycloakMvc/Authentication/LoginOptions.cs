using System.ComponentModel.DataAnnotations;

namespace KeycloakMvc.Authentication;

public sealed class LoginOptions
{
    public const string SectionName = "Login";

    /// <summary>Idle time before the application session expires (sliding).</summary>
    [Range(1, 1440)]
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Enables the Active Directory username/password page (/Account/Login). Off by default: the
    /// sign-in page offers only the Keycloak instances, and the password pages answer 404.
    /// </summary>
    public bool PasswordLoginEnabled { get; set; }

    /// <summary>Username/password attempts allowed per client IP address per minute.</summary>
    [Range(1, 100)]
    public int PasswordAttemptsPerMinute { get; set; } = 5;
}
