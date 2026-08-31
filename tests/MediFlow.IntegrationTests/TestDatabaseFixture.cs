namespace MediFlow.IntegrationTests;

using DotNet.Testcontainers.Builders;
using MediFlow.Infrastructure;
using MediFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

/// <summary>
/// One SQL Server container for the whole assembly: migrate → stored procedures →
/// the deterministic seed. Locally defaults to azure-sql-edge (arm64-native);
/// CI sets MEDIFLOW_TEST_SQL_IMAGE to mssql/server:2022 (x64 runners).
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    // The MsSql module's default wait strategy shells out to a host sqlcmd binary;
    // we wait on the container itself and poll the connection below instead.
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        Environment.GetEnvironmentVariable("MEDIFLOW_TEST_SQL_IMAGE") ?? "mcr.microsoft.com/azure-sql-edge:latest")
        .WithPassword("Test!Passw0rd")
        .WithWaitStrategy(Wait.ForUnixContainer())
        .Build();

    private ServiceProvider _services = null!;

    public string ConnectionString => _container.GetConnectionString();
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitForSqlAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MediFlowDb"] = ConnectionString,
                ["Database:InitializeOnStartup"] = "false",
                ["Seed:Enabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMediFlowInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediFlowDbContext>();
            await db.Database.MigrateAsync();
            await SqlScriptRunner.ApplyAsync(
                scope.ServiceProvider.GetRequiredService<MediFlow.Infrastructure.Data.IDbConnectionFactory>());
            await scope.ServiceProvider.GetRequiredService<MediFlowDataSeeder>().SeedAsync();
        }
    }

    public T Resolve<T>() where T : notnull => _services.GetRequiredService<T>();

    private async Task WaitForSqlAsync()
    {
        for (var attempt = 1; attempt <= 60; attempt++)
        {
            try
            {
                await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_container.GetConnectionString());
                await connection.OpenAsync();
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new TimeoutException("SQL container did not accept connections.");
    }

    /// <summary>Fresh, caller-owned context (tests dispose freely — the DI-resolved
    /// instance is shared and must not be disposed by tests).</summary>
    public MediFlowDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<MediFlowDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure(3))
            .Options);

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("database")]
public sealed class DatabaseCollectionFixture : ICollectionFixture<TestDatabaseFixture>;
