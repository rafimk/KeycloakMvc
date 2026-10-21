using KeycloakMvc.Authentication;
using KeycloakMvc.HealthChecks;
using KeycloakMvc.Logging;
using KeycloakMvc.Security;
using Serilog;

// Logs startup failures (for example invalid settings) before the configured logger exists.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddAppLogging();
    builder.Services.AddControllersWithViews();
    builder.Services.AddFrameAncestors();
    builder.Services.AddAppAuthentication();
    builder.Services.AddAppHealthChecks();

    var app = builder.Build();
    app.UseAppRequestLogging();
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.UseFrameAncestors();
    app.UseRouting();
    app.UseAppAuthentication();
    app.MapAppHealthChecks();
    app.MapStaticAssets().AllowAnonymous();
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The application failed to start or stopped unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
