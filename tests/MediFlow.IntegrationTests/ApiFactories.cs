namespace MediFlow.IntegrationTests;

using MediFlow.Api;
using MediFlow.Claims.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>MediFlow.Api host pointed at the test container. API key stays enforced —
/// tests exercise the real auth path.</summary>
public sealed class EnrollmentApiFactory(string connectionString) : WebApplicationFactory<EnrollmentApiEntryPoint>
{
    public const string ApiKey = "integration-test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings__MediFlowDb", connectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MediFlowDb"] = connectionString,
                ["Api:Required"] = "true",
                ["Api:Keys"] = ApiKey,
                ["Database:InitializeOnStartup"] = "false",
                ["Seed:Enabled"] = "false",
            });
        });
    }
}

/// <summary>MediFlow.Claims.Api host pointed at the test container.</summary>
public sealed class ClaimsApiFactory(string connectionString) : WebApplicationFactory<ClaimsApiEntryPoint>
{
    public const string ApiKey = "integration-test-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings__MediFlowDb", connectionString);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MediFlowDb"] = connectionString,
                ["Api:Required"] = "true",
                ["Api:Keys"] = ApiKey,
                ["Database:InitializeOnStartup"] = "false",
                ["Seed:Enabled"] = "false",
            });
        });
    }
}
