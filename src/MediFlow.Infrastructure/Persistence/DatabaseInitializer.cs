namespace MediFlow.Infrastructure.Persistence;

using Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Behavior flags for the database bootstrap hosted service.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Apply EF migrations and SQL objects on boot. Default true.</summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Run the bootstrap service at all — integration tests disable it
    /// and control the database explicitly.</summary>
    public bool InitializeOnStartup { get; set; } = true;
}

public sealed class SeedOptions
{
    /// <summary>Seed demonstration data when the database has no members.</summary>
    public bool Enabled { get; set; }

    /// <summary>Drop and recreate before seeding — resets demo drift.</summary>
    public bool Reset { get; set; }
}

/// <summary>
/// Boot-time bootstrap: waits for SQL availability, migrates, applies stored
/// procedures and (opt-in) seeds deterministic demo data. Config-gated so tests
/// and production behave differently from local demo runs.
/// </summary>
public sealed partial class DatabaseInitializer(
    IServiceProvider services,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<SeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    private const int AvailabilityRetries = 60;
    private static readonly TimeSpan AvailabilityRetryDelay = TimeSpan.FromSeconds(2);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying EF migrations")]
    private partial void LogMigrations();

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying stored procedures")]
    private partial void LogProcedures();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration race with a sibling service (SQL error {SqlError}); retry {Attempt}")]
    private static partial void LogMigrateRetry(ILogger logger, int attempt, int sqlError);

    [LoggerMessage(Level = LogLevel.Information, Message = "Existing data present — skipping seed")]
    private partial void LogSeedSkipped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding deterministic demonstration data")]
    private partial void LogSeeding();

    [LoggerMessage(Level = LogLevel.Information, Message = "Seed complete")]
    private partial void LogSeedComplete();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed__Reset requested — dropping and recreating the database")]
    private partial void LogReset();

    [LoggerMessage(Level = LogLevel.Information, Message = "Database not reachable yet (attempt {Attempt}/{Total})")]
    private static partial void LogWaiting(ILogger logger, int attempt, int total);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!databaseOptions.Value.InitializeOnStartup)
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediFlowDbContext>();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

        await WaitForDatabaseAsync(db, cancellationToken);

        if (databaseOptions.Value.AutoMigrate)
        {
            LogMigrations();
            // Several services boot concurrently in compose; only one wins the
            // CREATE DATABASE race, so retry the loser instead of crashing.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync(cancellationToken);
                    break;
                }
                catch (SqlException ex) when (attempt <= 5 && (ex.Number is 1801 or -2 or 4060 or 233))
                {
                    LogMigrateRetry(logger, attempt, ex.Number);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }

        LogProcedures();
        await SqlScriptRunner.ApplyAsync(connectionFactory, cancellationToken);

        if (seedOptions.Value.Enabled)
        {
            if (seedOptions.Value.Reset)
            {
                LogReset();
                await db.Database.EnsureDeletedAsync(cancellationToken);
                await db.Database.MigrateAsync(cancellationToken);
                await SqlScriptRunner.ApplyAsync(connectionFactory, cancellationToken);
            }

            if (await db.Members.AnyAsync(cancellationToken))
            {
                LogSeedSkipped();
                return;
            }

            LogSeeding();
            var seeder = scope.ServiceProvider.GetRequiredService<MediFlowDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
            LogSeedComplete();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Container startup races: SQL may still be coming online when the app boots.
    /// Probes against master — the target database does not exist until migrations run.
    /// </summary>
    private async Task WaitForDatabaseAsync(MediFlowDbContext db, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= AvailabilityRetries; attempt++)
        {
            try
            {
                if (await db.Database.CanConnectAsync(ct))
                {
                    return;
                }

                var configuration = services.GetRequiredService<IConfiguration>();
                var probeBuilder = new SqlConnectionStringBuilder(
                    configuration.GetConnectionString(SqlConnectionFactory.ConnectionStringName))
                {
                    InitialCatalog = "master",
                };
                await using var probe = new SqlConnection(probeBuilder.ConnectionString);
                await probe.OpenAsync(ct);
                return;
            }
            catch (SqlException)
            {
                // transient during container boot — retry below
            }

            LogWaiting(logger, attempt, AvailabilityRetries);
            await Task.Delay(AvailabilityRetryDelay, ct);
        }

        throw new InvalidOperationException($"Database did not become available after {AvailabilityRetries} attempts.");
    }
}
