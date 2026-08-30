namespace MediFlow.Infrastructure.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;

/// <summary>API-key authentication options (Api section).</summary>
public sealed class ApiKeyOptions
{
    /// <summary>Enforce X-Api-Key. Default true; local profiles set false for convenience.</summary>
    public bool Required { get; set; } = true;

    /// <summary>Comma-separated accepted keys. In production these come from Key Vault
    /// via environment variables — never from the repo.</summary>
    public string Keys { get; set; } = string.Empty;
}

/// <summary>
/// Rejects requests without a valid X-Api-Key using constant-time comparison.
/// Health/OpenAPI endpoints stay anonymous so probes can reach them.
/// Demo-grade by design; production replaces this with Entra ID OIDC (see docs/security.md).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private static readonly string[] AnonymousPrefixes = ["/health", "/alive", "/openapi", "/scalar"];

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<ApiKeyOptions> options)
    {
        if (!options.CurrentValue.Required || IsAnonymous(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
            !options.CurrentValue.Keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(expected => FixedTimeEquals(provided.ToString(), expected)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid X-Api-Key header is required.",
                Instance = context.Request.Path,
            });
            return;
        }

        await next(context);
    }

    private static bool IsAnonymous(PathString path) =>
        AnonymousPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Constant-time comparison — API keys are secrets, not passwords to bail on early.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);
        return left.Length == right.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
