using Serilog;
using Serilog.Events;

namespace KeycloakMvc.Logging;

public static class LoggingExtensions
{
    /// <summary>Replaces the default logging with Serilog, configured from the "Serilog" section.</summary>
    public static WebApplicationBuilder AddAppLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services));
        return builder;
    }

    /// <summary>
    /// One log line per request. Only the path is logged, never the query string, which can carry
    /// OIDC authorization codes. Health probes and static files are logged at Debug and Verbose.
    /// </summary>
    public static IApplicationBuilder UseAppRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : Path.HasExtension(context.Request.Path.Value) ? LogEventLevel.Debug
                : LogEventLevel.Information;
        });
}
