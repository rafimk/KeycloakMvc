using System.ComponentModel.DataAnnotations;

namespace KeycloakMvc.Authentication;

public sealed class LoginOptions
{
    public const string SectionName = "Login";

    /// <summary>Idle time before the application session expires (sliding).</summary>
    [Range(1, 1440)]
    public int SessionTimeoutMinutes { get; set; } = 60;

    /// <summary>Username/password attempts allowed per client IP address per minute.</summary>
    [Range(1, 100)]
    public int PasswordAttemptsPerMinute { get; set; } = 5;
}
