using System.Security.Claims;
using System.Threading.RateLimiting;
using KeycloakMvc.Authentication;
using KeycloakMvc.Authentication.ActiveDirectory;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
// Framing is controlled by the CSP frame-ancestors header below instead.
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
var frameAncestors = string.Join(' ', builder.Configuration.GetSection("FrameAncestors").Get<string[]>() ?? []);
var requireHttps = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", true);

builder.Services.AddOptions<ActiveDirectoryOptions>()
    .BindConfiguration(ActiveDirectoryOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ActiveDirectoryOptions>, ActiveDirectoryOptionsValidator>();
builder.Services.AddSingleton<IActiveDirectoryService, ActiveDirectoryService>();

// Anonymous users get the cookie challenge (the selection page). Keycloak is only
// reached when the user explicitly chooses it in AccountController.LoginWithLoggedUser.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/SelectAuthentication";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(1);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    // The iframe host page is same-site (another localhost port), so Lax works and still blocks
    // cross-site POSTs. Embedding on another site needs SameSite=None over HTTPS.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
}).AddOpenIdConnect(options =>
{
    var settings = builder.Configuration.GetSection("Keycloak");
    options.Authority = settings["Authority"]!;
    options.MetadataAddress = settings["MetadataAddress"] ??
        $"{options.Authority}/.well-known/openid-configuration";
    options.ClientId = settings["ClientId"]!;
    options.ClientSecret = settings["ClientSecret"]!;
    options.RequireHttpsMetadata = settings.GetValue("RequireHttpsMetadata", true);
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.MapInboundClaims = false;
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = AppClaimTypes.Role;
    options.TokenValidationParameters.ValidIssuer = options.Authority;
    options.SaveTokens = true;
    // Local HTTP sample only. Use HTTPS and default secure cookies in production.
    if (!options.RequireHttpsMetadata)
    {
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }
    // Optional: the amr value that a Keycloak AMR mapper emits for the Kerberos execution.
    // Without it the app cannot prove Kerberos was used and reports plain Keycloak SSO.
    var kerberosAmr = settings["KerberosAmrValue"];
    options.Events.OnTokenValidated = context =>
    {
        // Runs after Keycloak's signature, issuer, audience, nonce and PKCE checks have passed.
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        var token = context.SecurityToken;
        var method = !string.IsNullOrEmpty(kerberosAmr) && identity.HasClaim("amr", kerberosAmr)
            ? AppAuthenticationMethods.KeycloakKerberos
            : AppAuthenticationMethods.KeycloakSso;
        identity.AddClaims(
        [
            new(AppClaimTypes.UserId, $"keycloak|{token.Issuer}|{token.Subject}"),
            new(AppClaimTypes.AuthenticationMethod, method),
            new(AppClaimTypes.AuthenticationDomain, token.Issuer),
        ]);
        return Task.CompletedTask;
    };
    options.Events.OnRemoteFailure = context =>
    {
        // The handler already logs the failure. Show a friendly page instead of an exception,
        // and do not fall back to Active Directory.
        context.Response.Redirect("/Account/LoginFailed");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

// Every page requires a signed-in user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    // Password attempts per client IP. Per-account lockout is enforced by Active Directory.
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Redirect("/Account/Login?throttled=true");
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use((context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = $"frame-ancestors 'self' {frameAncestors}".TrimEnd();
    return next(context);
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
