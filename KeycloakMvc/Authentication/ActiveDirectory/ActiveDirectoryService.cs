using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.Authentication.ActiveDirectory;

/// <summary>
/// Verifies a password with an LDAPS simple bind against the configured domain controller, then
/// reads the user's own directory entry with that connection. No service account is needed and
/// nothing is cached.
/// </summary>
public sealed partial class ActiveDirectoryService(
    IOptions<ActiveDirectoryOptions> options,
    ILogger<ActiveDirectoryService> logger) : IActiveDirectoryService
{
    private const int InvalidCredentialsResultCode = 49;
    // userAccountControl ACCOUNTDISABLE flag.
    private const int AccountDisabledFlag = 0x2;
    private static readonly char[] InvalidAccountNameChars =
        ['"', '/', '\\', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>', '@'];
    private static readonly string[] UserAttributes =
        ["objectGUID", "sAMAccountName", "displayName", "userAccountControl", "memberOf"];

    public async Task<ActiveDirectoryAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        string domain,
        CancellationToken cancellationToken = default)
    {
        // Only configured domains are accepted, so users can never point the app at another LDAP server.
        var settings = options.Value.Domains.FirstOrDefault(d =>
            string.Equals(d.Domain, domain, StringComparison.OrdinalIgnoreCase));
        if (settings is null)
        {
            logger.LogWarning("Active Directory sign-in rejected: domain {Domain} is not configured", domain);
            return Fail(ActiveDirectoryAuthenticationStatus.InvalidRequest);
        }

        // An LDAP simple bind with an empty password is an anonymous bind and would "succeed".
        if (string.IsNullOrEmpty(password) || !TryNormalizeUserName(username, settings, out var accountName))
        {
            logger.LogWarning("Active Directory sign-in rejected: malformed request for domain {Domain}", settings.Domain);
            return Fail(ActiveDirectoryAuthenticationStatus.InvalidRequest);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = ActiveDirectoryConnectionFactory.Create(settings, AuthType.Basic);
            try
            {
                // System.DirectoryServices.Protocols has no asynchronous bind. This call blocks
                // for at most the configured timeout; the lookup below is truly asynchronous.
                connection.Bind(new NetworkCredential($"{accountName}@{settings.Domain}", password));
            }
            catch (LdapException ex) when (ex.ErrorCode == InvalidCredentialsResultCode)
            {
                // The AD sub-code (52e bad password, 533 disabled, 775 locked, ...) is logged, never shown.
                logger.LogInformation(
                    "Active Directory rejected credentials for {UserName} in {Domain} (reason {ReasonCode})",
                    accountName, settings.Domain, GetReasonCode(ex.ServerErrorMessage) ?? "unknown");
                return Fail(ActiveDirectoryAuthenticationStatus.InvalidCredentials);
            }

            var entry = await FindUserAsync(connection, settings, accountName, cancellationToken);
            if (entry is null)
            {
                logger.LogWarning("Active Directory bind succeeded but {UserName} was not found once in {Domain}",
                    accountName, settings.Domain);
                return Fail(ActiveDirectoryAuthenticationStatus.NotPermitted);
            }

            if (int.TryParse(GetString(entry, "userAccountControl"), out var flags) && (flags & AccountDisabledFlag) != 0)
            {
                logger.LogWarning("Active Directory account {UserName} in {Domain} is disabled", accountName, settings.Domain);
                return Fail(ActiveDirectoryAuthenticationStatus.NotPermitted);
            }

            var objectGuid = GetBytes(entry, "objectGUID");
            if (objectGuid is not { Length: 16 })
            {
                logger.LogWarning("Active Directory entry for {UserName} in {Domain} has no objectGUID",
                    accountName, settings.Domain);
                return Fail(ActiveDirectoryAuthenticationStatus.NotPermitted);
            }

            var groups = GetStrings(entry, "memberOf").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var roles = settings.RoleMappings.Where(m => groups.Contains(m.Group))
                .Select(m => m.Role).Distinct(StringComparer.Ordinal).ToList();
            var user = new ActiveDirectoryUser(
                new Guid(objectGuid).ToString(),
                GetString(entry, "sAMAccountName") ?? accountName,
                GetString(entry, "displayName"),
                settings.Domain.ToLowerInvariant(),
                roles);
            logger.LogInformation("Active Directory authenticated {UserName} in {Domain}", user.UserName, user.Domain);
            return ActiveDirectoryAuthenticationResult.Success(user);
        }
        catch (LdapException ex)
        {
            // 81 server down, 85 timeout, 91 connect error, or a TLS/certificate failure.
            logger.LogWarning("Active Directory domain {Domain} at {Server}:{Port} is unavailable (LDAP {ErrorCode}: {Message})",
                settings.Domain, settings.Server, settings.Port, ex.ErrorCode, ex.Message);
            return Fail(ActiveDirectoryAuthenticationStatus.DomainUnavailable);
        }
        catch (DirectoryOperationException ex)
        {
            logger.LogWarning("Active Directory lookup failed in {Domain} ({ResultCode})",
                settings.Domain, ex.Response?.ResultCode);
            return Fail(ActiveDirectoryAuthenticationStatus.DomainUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // For example a missing native libldap on Linux. The exception never contains the password.
            logger.LogError(ex, "Active Directory authentication against {Domain} failed unexpectedly", settings.Domain);
            return Fail(ActiveDirectoryAuthenticationStatus.DomainUnavailable);
        }
    }

    /// <summary>
    /// Accepts "user", "user@dns.domain", "NETBIOS\user" or "dns.domain\user", but only when any
    /// domain part matches the selected domain, so a name can never resolve in a different domain.
    /// </summary>
    internal static bool TryNormalizeUserName(string input, ActiveDirectoryDomainOptions domain, out string accountName)
    {
        accountName = string.Empty;
        var value = input.Trim();
        var backslash = value.IndexOf('\\');
        if (backslash >= 0)
        {
            var prefix = value[..backslash];
            if (!prefix.Equals(domain.NetBiosName, StringComparison.OrdinalIgnoreCase) &&
                !prefix.Equals(domain.Domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            value = value[(backslash + 1)..];
        }
        else if (value.LastIndexOf('@') is var at and >= 0)
        {
            if (!value[(at + 1)..].Equals(domain.Domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            value = value[..at];
        }

        if (value.Length is 0 or > 64 || value.EndsWith('.') ||
            value.IndexOfAny(InvalidAccountNameChars) >= 0 || value.Any(char.IsControl))
        {
            return false;
        }
        accountName = value;
        return true;
    }

    private static async Task<SearchResultEntry?> FindUserAsync(
        LdapConnection connection, ActiveDirectoryDomainOptions settings, string accountName, CancellationToken cancellationToken)
    {
        var request = new SearchRequest(
            settings.SearchBase ?? string.Join(',', settings.Domain.Split('.').Select(part => $"DC={part}")),
            $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeFilterValue(accountName)}))",
            SearchScope.Subtree,
            UserAttributes)
        {
            SizeLimit = 2,
        };
        var response = await ActiveDirectoryConnectionFactory.SearchAsync(connection, request, cancellationToken);
        return response.Entries.Count == 1 ? response.Entries[0] : null;
    }

    /// <summary>RFC 4515 escaping for a value inside an LDAP search filter.</summary>
    internal static string EscapeFilterValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString(),
            });
        }
        return builder.ToString();
    }

    private static string? GetReasonCode(string? serverMessage) =>
        serverMessage is null ? null : ReasonCodePattern().Match(serverMessage) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? GetString(SearchResultEntry entry, string attribute) =>
        GetStrings(entry, attribute).FirstOrDefault();

    private static IEnumerable<string> GetStrings(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute]?.GetValues(typeof(string)).Cast<string>() ?? [];

    private static byte[]? GetBytes(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute]?.GetValues(typeof(byte[])).Cast<byte[]>().FirstOrDefault();

    private static ActiveDirectoryAuthenticationResult Fail(ActiveDirectoryAuthenticationStatus status) =>
        ActiveDirectoryAuthenticationResult.Failure(status);

    [GeneratedRegex(@"\bdata ([0-9a-f]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReasonCodePattern();
}
