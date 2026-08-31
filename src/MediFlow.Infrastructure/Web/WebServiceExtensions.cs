namespace MediFlow.Infrastructure.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.AspNetCore;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.RateLimiting;

public static class WebServiceExtensions
{
    /// <summary>
    /// Shared service-host plumbing for both REST APIs: Serilog, OpenAPI + Scalar,
    /// rate limiting, ProblemDetails, health checks, OpenTelemetry and API-key
    /// options. One implementation so the two services can never drift (ADR 0007).
    /// </summary>
    [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Composition root")]
    public static TBuilder AddMediFlowWebService<TBuilder>(this TBuilder builder, string serviceName)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSerilog(cfg => cfg
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {Message:lj}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.InvariantCulture));

        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddHttpLogging(options => { });

        // Fixed-window limiter: 100 requests/minute per API key (anonymous bucket for
        // health checks). Protection, not billing - production tunes per consumer via
        // RateLimit:PermitLimit. The global limiter covers every endpoint without
        // per-route opt-in, so the documented 429 mitigation cannot go inert again.
        builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
        builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        builder.Services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitOptions>>((options, rateLimit) =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    var apiKey = context.Request.Headers["X-Api-Key"].ToString();
                    var partition = apiKey.Length > 0 ? $"key:{apiKey}" : $"ip:{context.Connection.RemoteIpAddress}";
                    return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimit.Value.PermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
                });
            });

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<MediFlowDbContext>("medi flow-db", tags: ["ready"]);

        // Traces + metrics ship to OTLP when an endpoint is configured (compose/Azure),
        // otherwise they stay local - Serilog owns the console.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("MediFlow.Worker"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
            builder.Services.AddOpenTelemetry()
                .WithTracing(t => t.AddOtlpExporter())
                .WithMetrics(m => m.AddOtlpExporter());
        }

        builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("Api"));
        return builder;
    }

    /// <summary>Shared middleware pipeline for both REST APIs.</summary>
    public static WebApplication UseMediFlowWebService(this WebApplication app, string serviceName)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        app.Use(async (context, next) =>
        {
            // Baseline hardening headers on every response (docs/security.md).
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseRateLimiter();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthJson,
        });

        app.MapOpenApi();
        app.MapScalarApiReference(options => options.Title = $"{serviceName} API");

        return app;
    }

    private static Task WriteHealthJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() }),
        });
        return context.Response.WriteAsync(payload);
    }
}

/// <summary>Rate limiter options (RateLimit section). Defaults reproduce the
/// documented 100 requests/minute fixed window; tests shrink the limit to
/// observe 429 behavior without waiting a full window.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Permits per 1-minute window, per API key (per IP for anonymous paths).</summary>
    public int PermitLimit { get; set; } = 100;
}
