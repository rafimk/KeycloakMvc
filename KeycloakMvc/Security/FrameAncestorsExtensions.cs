using Microsoft.Extensions.Options;

namespace KeycloakMvc.Security;

public static class FrameAncestorsExtensions
{
    public static IServiceCollection AddFrameAncestors(this IServiceCollection services)
    {
        services.AddOptions<FrameAncestorsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.Origins = configuration.GetSection(FrameAncestorsOptions.SectionName).Get<string[]>() ?? [])
            .Validate(options => options.Origins.All(IsOrigin),
                $"{FrameAncestorsOptions.SectionName} entries must be origins such as https://host.example.com.")
            .ValidateOnStart();
        // Framing is controlled by the CSP frame-ancestors header below instead of X-Frame-Options.
        services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);
        return services;
    }

    /// <summary>Sends Content-Security-Policy: frame-ancestors 'self' plus the configured origins.</summary>
    public static IApplicationBuilder UseFrameAncestors(this IApplicationBuilder app)
    {
        var origins = app.ApplicationServices.GetRequiredService<IOptions<FrameAncestorsOptions>>().Value.Origins;
        var policy = $"frame-ancestors 'self' {string.Join(' ', origins)}".TrimEnd();
        return app.Use((context, next) =>
        {
            context.Response.Headers.ContentSecurityPolicy = policy;
            return next(context);
        });
    }

    private static bool IsOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && !value.EndsWith('/');
}
