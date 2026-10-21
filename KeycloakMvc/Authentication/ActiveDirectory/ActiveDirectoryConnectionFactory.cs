using System.DirectoryServices.Protocols;

namespace KeycloakMvc.Authentication.ActiveDirectory;

/// <summary>
/// Creates LDAPS connections to a configured domain controller. The server certificate is validated
/// by the operating system (Schannel on Windows, libldap/OpenSSL on Linux); this code never overrides
/// that validation.
/// </summary>
internal static class ActiveDirectoryConnectionFactory
{
    public static LdapConnection Create(ActiveDirectoryDomainOptions settings, AuthType authType)
    {
        var connection = new LdapConnection(
            new LdapDirectoryIdentifier(settings.Server, settings.Port, fullyQualifiedDnsHostName: true, connectionless: false))
        {
            AuthType = authType,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = settings.UseSsl;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        return connection;
    }

    /// <summary>Sends a search with the library's asynchronous (Begin/End) API.</summary>
    public static async Task<SearchResponse> SearchAsync(
        LdapConnection connection, SearchRequest request, CancellationToken cancellationToken) =>
        (SearchResponse)await Task.Factory.FromAsync(
            (callback, state) => connection.BeginSendRequest(request, PartialResultProcessing.NoPartialResultSupport, callback, state),
            connection.EndSendRequest,
            state: null).WaitAsync(cancellationToken);
}
