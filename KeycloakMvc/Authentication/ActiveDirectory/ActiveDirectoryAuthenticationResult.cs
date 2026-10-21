namespace KeycloakMvc.Authentication.ActiveDirectory;

public enum ActiveDirectoryAuthenticationStatus
{
    Succeeded,
    /// <summary>Unknown domain or a username that does not belong to the selected domain.</summary>
    InvalidRequest,
    /// <summary>Wrong username or password, or the directory refused the bind (locked, expired, disabled).</summary>
    InvalidCredentials,
    /// <summary>The bind succeeded but the account is disabled or could not be resolved.</summary>
    NotPermitted,
    /// <summary>The domain controller could not be reached or failed to answer in time.</summary>
    DomainUnavailable,
}

/// <summary>A user verified by Active Directory. Never contains the password.</summary>
public sealed record ActiveDirectoryUser(
    string ObjectGuid,
    string UserName,
    string? DisplayName,
    string Domain,
    IReadOnlyList<string> Roles);

public sealed class ActiveDirectoryAuthenticationResult
{
    private ActiveDirectoryAuthenticationResult(ActiveDirectoryAuthenticationStatus status, ActiveDirectoryUser? user)
    {
        Status = status;
        User = user;
    }

    public ActiveDirectoryAuthenticationStatus Status { get; }

    public ActiveDirectoryUser? User { get; }

    public bool Succeeded => Status == ActiveDirectoryAuthenticationStatus.Succeeded && User is not null;

    public static ActiveDirectoryAuthenticationResult Success(ActiveDirectoryUser user) =>
        new(ActiveDirectoryAuthenticationStatus.Succeeded, user);

    public static ActiveDirectoryAuthenticationResult Failure(ActiveDirectoryAuthenticationStatus status) =>
        status == ActiveDirectoryAuthenticationStatus.Succeeded
            ? throw new ArgumentException("A failure needs a failure status.", nameof(status))
            : new(status, null);
}
