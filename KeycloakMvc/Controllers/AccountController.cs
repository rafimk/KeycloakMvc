using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KeycloakMvc.Controllers;
public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login() => Challenge(
        new AuthenticationProperties { RedirectUri = "/Account/Profile" },
        OpenIdConnectDefaults.AuthenticationScheme);
    [Authorize]
    public IActionResult Profile() => View();
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme);
}
