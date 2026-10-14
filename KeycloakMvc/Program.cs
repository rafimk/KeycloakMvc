using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
// Framing is controlled by the CSP frame-ancestors header below instead.
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
var frameAncestors = string.Join(' ', builder.Configuration.GetSection("FrameAncestors").Get<string[]>() ?? []);
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
}).AddCookie().AddOpenIdConnect(options =>
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
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
