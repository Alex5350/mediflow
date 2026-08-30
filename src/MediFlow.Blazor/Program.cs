using MediFlow.Blazor.Components;
using MediFlow.Blazor.Services;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .MinimumLevel.Information()
    .Enrich.WithProperty("Service", "mediflow-blazor")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {Message:lj}{NewLine}{Exception}",
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHealthChecks();

// The dashboard is a REST consumer of the two APIs — typed clients with the
// standard resilience handler (retry + circuit breaker) and the API key attached.
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
builder.Services.AddTransient<ApiKeyHandler>();
builder.Services.AddHttpClient<EnrollmentApiClient>((sp, http) =>
    {
        http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ApiOptions>>().Value.EnrollmentBaseUrl);
    })
    .AddHttpMessageHandler<ApiKeyHandler>()
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<ClaimsApiClient>((sp, http) =>
    {
        http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ApiOptions>>().Value.ClaimsBaseUrl);
    })
    .AddHttpMessageHandler<ApiKeyHandler>()
    .AddStandardResilienceHandler();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/live");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Test host entry point.</summary>
public partial class Program { }
