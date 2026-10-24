using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using KeycloakMvc.Authentication;
using KeycloakMvc.Models;

namespace KeycloakMvc.Controllers;

public class HomeController : Controller
{
    [Authorize]
    public IActionResult Index()
    {
        var method = User.FindFirstValue(AppClaimTypes.AuthenticationMethod);
        return View(new HomeViewModel
        {
            UserName = User.Identity?.Name ?? string.Empty,
            DisplayName = User.FindFirstValue(AppClaimTypes.DisplayName),
            AuthenticationMethod = method switch
            {
                AppAuthenticationMethods.ActiveDirectory => "Active Directory",
                AppAuthenticationMethods.KeycloakKerberos => "Keycloak Kerberos SSO",
                _ => "Keycloak SSO",
            },
            IsActiveDirectory = method == AppAuthenticationMethods.ActiveDirectory,
            KeycloakInstance = User.FindFirstValue(AppClaimTypes.KeycloakInstance),
            Domain = User.FindFirstValue(AppClaimTypes.AuthenticationDomain),
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
