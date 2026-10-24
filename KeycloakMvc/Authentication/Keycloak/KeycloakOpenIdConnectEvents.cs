using System.Security.Claims;
using KeycloakMvc.Authentication.PermittedUsers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace KeycloakMvc.Authentication.Keycloak;

/// <summary>
/// Adds the application's claims to Keycloak sign-ins and enforces the permitted users list.
/// Shared by every Keycloak instance; the scheme name tells which one is signing in.
/// </summary>
public sealed class KeycloakOpenIdConnectEvents(
    IOptionsMonitor<KeycloakOptions> keycloakOptions,
    IPermittedUserService permittedUsers,
    ILogger<KeycloakOpenIdConnectEvents> logger) : OpenIdConnectEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        // Runs after Keycloak's signature, issuer, audience, nonce and PKCE checks have passed.
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        var token = context.SecurityToken;
        var instance = KeycloakInstances.FindByScheme(context.Scheme.Name);
        var kerberosAmr = keycloakOptions.Get(instance.Scheme).KerberosAmrValue;
        var method = !string.IsNullOrEmpty(kerberosAmr) && identity.HasClaim("amr", kerberosAmr)
            ? AppAuthenticationMethods.KeycloakKerberos
            : AppAuthenticationMethods.KeycloakSso;
        identity.AddClaims(
        [
            new(AppClaimTypes.UserId, $"keycloak|{token.Issuer}|{token.Subject}"),
            new(AppClaimTypes.AuthenticationMethod, method),
            new(AppClaimTypes.AuthenticationDomain, token.Issuer),
            new(AppClaimTypes.KeycloakInstance, instance.Name),
        ]);

        if (!permittedUsers.IsPermitted(context.Principal))
        {
            logger.LogWarning("Keycloak {Instance} user {UserName} signed in but is not a permitted user",
                instance.Name, identity.Name);
            await DenyAndEndKeycloakSessionAsync(context);
        }
    }

    public override Task RemoteFailure(RemoteFailureContext context)
    {
        // The handler already logs the failure. Show a friendly page instead of an exception,
        // and do not fall back to Active Directory.
        context.Response.Redirect(AuthenticationConstants.LoginFailedPath);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates no local session and also ends the Keycloak session, so the user can sign in with a
    /// different account. Keycloak returns to the handler's sign-out callback, which then redirects
    /// to the Not authorized page.
    /// </summary>
    private static async Task DenyAndEndKeycloakSessionAsync(TokenValidatedContext context)
    {
        var options = context.Options;
        var configuration = options.ConfigurationManager is null
            ? null
            : await options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        context.HandleResponse();
        if (string.IsNullOrEmpty(configuration?.EndSessionEndpoint))
        {
            context.Response.Redirect(AuthenticationConstants.NotAuthorizedPath);
            return;
        }

        var request = context.Request;
        var logout = new OpenIdConnectMessage
        {
            IssuerAddress = configuration.EndSessionEndpoint,
            ClientId = options.ClientId,
            IdTokenHint = context.TokenEndpointResponse?.IdToken,
            PostLogoutRedirectUri = $"{request.Scheme}://{request.Host}{request.PathBase}{options.SignedOutCallbackPath}",
            State = options.StateDataFormat.Protect(
                new AuthenticationProperties { RedirectUri = AuthenticationConstants.NotAuthorizedPath }),
        };
        context.Response.Redirect(logout.CreateLogoutRequestUrl());
    }
}
