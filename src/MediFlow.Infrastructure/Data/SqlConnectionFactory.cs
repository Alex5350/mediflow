namespace MediFlow.Infrastructure.Data;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data.Common;

/// <summary>Creates open, caller-owned <see cref="SqlConnection"/>s for the Dapper layer.</summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    public const string ConnectionStringName = "MediFlowDb";

    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured."));

        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
