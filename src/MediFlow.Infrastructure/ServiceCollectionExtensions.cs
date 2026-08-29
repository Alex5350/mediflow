namespace MediFlow.Infrastructure;

using Claims;
using Dapper;
using Data;
using Enrollment;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Domain.Enrollment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MediFlow data layer: EF Core context, Dapper read store,
    /// claim intake, enrollment decisions, and the adjudication engine with its
    /// ordered claim-rule chain (rule order is semantic — see ADR 0002).
    /// </summary>
    public static IServiceCollection AddMediFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved lazily so host-level configuration overrides (integration-test
        // factories, compose env) land before the connection string is read.
        services.AddDbContext<MediFlowDbContext>((sp, options) =>
            options.UseSqlServer(
                sp.GetRequiredService<IConfiguration>().GetConnectionString(SqlConnectionFactory.ConnectionStringName)
                    ?? throw new InvalidOperationException("Connection string 'MediFlowDb' is not configured."),
                sql =>
                {
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                    sql.CommandTimeout(30);
                }));

        // Dapper maps SQL date → DateOnly (see Data/DateOnlyTypeHandler).
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IReadStore, DapperReadStore>();
        services.AddScoped<IClaimIntakeService, ClaimIntakeService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAdjudicationGateway, AdjudicationGateway>();
        services.AddScoped<IClaimDetailsService, ClaimDetailsService>();
        services.AddScoped<IClaimAdjudicationRunner, ClaimAdjudicationRunner>();

        // Registered as the interface so AdjudicationEngine's IEnumerable<IAdjudicationClaimRule>
        // resolves them in registration order — the order IS the rule pipeline.
        services.AddScoped<IAdjudicationClaimRule, FilingTimelinessRule>();
        services.AddScoped<IAdjudicationClaimRule, CoverageRule>();
        services.AddScoped<IAdjudicationClaimRule, DuplicateClaimRule>();
        services.AddScoped<AdjudicationEngine>();

        services.AddScoped<MediFlowDataSeeder>();

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration("Database")
            .ValidateOnStart();
        services.AddOptions<SeedOptions>()
            .BindConfiguration("Seed");

        services.AddHostedService<DatabaseInitializer>();
        return services;
    }
}
