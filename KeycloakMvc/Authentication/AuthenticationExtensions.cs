using System.Threading.RateLimiting;
using KeycloakMvc.Authentication.ActiveDirectory;
using KeycloakMvc.Authentication.Keycloak;
using KeycloakMvc.Authentication.PermittedUsers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace KeycloakMvc.Authentication;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Registers sign-in: a local cookie session created by one of the Keycloak instances (OIDC),
    /// limited to the permitted users list. The Active Directory password check stays registered,
    /// but its pages answer 404 unless Login:PasswordLoginEnabled is true.
    /// </summary>
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        // Each instance's settings are named options, keyed by its OIDC scheme name.
        foreach (var instance in KeycloakInstances.All)
        {
            services.AddOptions<KeycloakOptions>(instance.Scheme)
                .BindConfiguration(instance.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
        services.AddOptions<LoginOptions>()
            .BindConfiguration(LoginOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ActiveDirectoryOptions>()
            .BindConfiguration(ActiveDirectoryOptions.SectionName)
            .ValidateOnStart();
        services.AddOptions<PermittedUsersOptions>()
            .BindConfiguration(PermittedUsersOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ActiveDirectoryOptions>, ActiveDirectoryOptionsValidator>();
        services.AddSingleton<IValidateOptions<PermittedUsersOptions>, PermittedUsersOptionsValidator>();

        services.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();
        services.AddSingleton<IPermittedUserService, PermittedUserService>();
        services.AddScoped<AppCookieEvents>();
        services.AddScoped<KeycloakOpenIdConnectEvents>();

        // Anonymous users get the cookie challenge (the selection page). A Keycloak instance is only
        // reached when the user explicitly chooses it in AccountController.LoginWithKeycloak.
        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<LoginOptions>, IHostEnvironment>(ConfigureCookie);
        foreach (var instance in KeycloakInstances.All)
        {
            authentication.AddOpenIdConnect(instance.Scheme, _ => { });
            services.AddOptions<OpenIdConnectOptions>(instance.Scheme)
                .Configure<IOptionsMonitor<KeycloakOptions>>((options, keycloak) =>
                    ConfigureKeycloak(options, instance, keycloak.Get(instance.Scheme)));
        }

        // Every page requires a signed-in user unless it opts out with [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(options =>
        {
            // Password attempts per client IP. Per-account lockout is enforced by Active Directory.
            options.AddPolicy(AuthenticationConstants.PasswordLoginRateLimitPolicy, context =>
            {
                var login = context.RequestServices.GetRequiredService<IOptions<LoginOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = login.PasswordAttemptsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    });
            });
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Redirect($"{AuthenticationConstants.PasswordLoginPath}?throttled=true");
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    /// <summary>Adds rate limiting, authentication and authorization. Call after UseRouting.</summary>
    public static IApplicationBuilder UseAppAuthentication(this IApplicationBuilder app) =>
        app.UseRateLimiter()
            .UseAuthentication()
            .UseAuthorization();

    private static void ConfigureCookie(
        CookieAuthenticationOptions options, IOptions<LoginOptions> loginOptions, IHostEnvironment environment)
    {
        options.LoginPath = AuthenticationConstants.SelectAuthenticationPath;
        options.AccessDeniedPath = AuthenticationConstants.AccessDeniedPath;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(loginOptions.Value.SessionTimeoutMinutes);
        options.SlidingExpiration = true;
        options.EventsType = typeof(AppCookieEvents);
        options.Cookie.HttpOnly = true;
        // The iframe host page is same-site (another localhost port), so Lax works and still blocks
        // cross-site POSTs. Embedding on another site needs SameSite=None over HTTPS.
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Local HTTP sample only. Outside Development the cookie is sent over HTTPS only.
        options.Cookie.SecurePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    }

    private static void ConfigureKeycloak(OpenIdConnectOptions options, KeycloakInstance instance, KeycloakOptions keycloak)
    {
        // Separate paths, so each handler only answers callbacks from its own Keycloak instance.
        options.CallbackPath = instance.CallbackPath;
        options.SignedOutCallbackPath = instance.SignedOutCallbackPath;
        options.RemoteSignOutPath = instance.RemoteSignOutPath;
        options.Authority = keycloak.Authority;
        options.MetadataAddress = keycloak.DiscoveryDocumentUrl;
        options.ClientId = keycloak.ClientId;
        options.ClientSecret = keycloak.ClientSecret;
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters.NameClaimType = AppClaimTypes.UserName;
        options.TokenValidationParameters.RoleClaimType = AppClaimTypes.Role;
        options.TokenValidationParameters.ValidIssuer = keycloak.Authority;
        options.SaveTokens = true;
        options.EventsType = typeof(KeycloakOpenIdConnectEvents);
        // Local HTTP sample only. Use HTTPS and default secure cookies in production.
        if (!options.RequireHttpsMetadata)
        {
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
    }
}
