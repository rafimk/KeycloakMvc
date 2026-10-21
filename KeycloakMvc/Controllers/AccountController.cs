using System.Security.Claims;
using KeycloakMvc.Authentication;
using KeycloakMvc.Authentication.ActiveDirectory;
using KeycloakMvc.Authentication.PermittedUsers;
using KeycloakMvc.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KeycloakMvc.Controllers;
public class AccountController(
    IActiveDirectoryService activeDirectory,
    IPermittedUserService permittedUsers,
    IOptions<ActiveDirectoryOptions> activeDirectoryOptions,
    ILogger<AccountController> logger) : Controller
{
    private const string InvalidCredentialsMessage = "The username, password or domain is incorrect.";
    private const string UnavailableMessage = "Sign-in is temporarily unavailable. Try again later.";

    [AllowAnonymous, HttpGet]
    public IActionResult SelectAuthentication(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl(returnUrl));
        return View(new SelectAuthenticationViewModel
        {
            ReturnUrl = SafeReturnUrl(returnUrl),
            PasswordLoginEnabled = activeDirectoryOptions.Value.Domains.Count > 0,
        });
    }

    // Starts the existing Keycloak OIDC flow. Keycloak's browser flow decides whether Kerberos is used,
    // and KeycloakOpenIdConnectEvents enforces the permitted users list.
    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public IActionResult LoginWithLoggedUser(string? returnUrl = null) => Challenge(
        new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) },
        OpenIdConnectDefaults.AuthenticationScheme);

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null, bool throttled = false)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(SafeReturnUrl(returnUrl));
        if (throttled)
            ModelState.AddModelError(string.Empty, "Too many sign-in attempts. Wait a minute and try again.");
        var model = new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) };
        if (activeDirectoryOptions.Value.Domains.Count == 1)
            model.Domain = activeDirectoryOptions.Value.Domains[0].Domain;
        return LoginView(model);
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationConstants.PasswordLoginRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!activeDirectoryOptions.Value.Domains.Any(d => string.Equals(d.Domain, model.Domain, StringComparison.OrdinalIgnoreCase)))
            ModelState.AddModelError(nameof(LoginViewModel.Domain), "Select a domain from the list.");
        if (!ModelState.IsValid)
            return LoginView(model);

        var result = await activeDirectory.AuthenticateAsync(model.UserName, model.Password, model.Domain, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status == ActiveDirectoryAuthenticationStatus.DomainUnavailable
                ? UnavailableMessage
                : InvalidCredentialsMessage);
            return LoginView(model);
        }

        var principal = AppPrincipalFactory.CreateActiveDirectoryPrincipal(result.User!);
        if (!permittedUsers.IsPermitted(principal))
        {
            logger.LogWarning("Active Directory user {UserName} in {Domain} signed in but is not a permitted user",
                result.User!.UserName, result.User.Domain);
            return RedirectToAction(nameof(NotAuthorized));
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return LocalRedirect(model.ReturnUrl);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult LoginFailed() => View();

    // Shown when sign-in succeeded but the user is not on the permitted users list. No session exists.
    [AllowAnonymous, HttpGet]
    public IActionResult NotAuthorized() => View();

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();

    [Authorize]
    public IActionResult Profile() => View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var afterLogout = Url.Action(nameof(SelectAuthentication))!;
        if (User.FindFirstValue(AppClaimTypes.AuthenticationMethod) == AppAuthenticationMethods.ActiveDirectory)
        {
            // AD sessions are local only; there is no Keycloak session to end.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect(afterLogout);
        }
        return SignOut(
            new AuthenticationProperties { RedirectUri = afterLogout },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

    private ViewResult LoginView(LoginViewModel model)
    {
        model.Password = string.Empty;
        model.Domains = activeDirectoryOptions.Value.Domains
            .Select(d => new SelectListItem($"{d.Name} ({d.Domain})", d.Domain))
            .ToList();
        return View(nameof(Login), model);
    }
}
