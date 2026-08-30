namespace MediFlow.Infrastructure.Persistence;

using Dapper;
using Data;
using Microsoft.Data.SqlClient;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Applies the embedded SQL objects (table type + stored procedures) in filename
/// order. Idempotent — every script uses CREATE OR ALTER / IF NOT EXISTS guards —
/// so it runs on every boot after EF migrations.
/// </summary>
public static partial class SqlScriptRunner
{
    [GeneratedRegex(@"^GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GoSeparator();

    public static async Task ApplyAsync(IDbConnectionFactory connectionFactory, CancellationToken ct = default)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("MediFlow.Infrastructure.Sql.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Order()
            .ToList();

        await using var connection = await connectionFactory.CreateOpenConnectionAsync(ct);
        foreach (var resource in resources)
        {
            await using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded SQL resource {resource} not found.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);

            foreach (var batch in GoSeparator().Split(sql).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await connection.ExecuteAsync(new CommandDefinition(batch, cancellationToken: ct));
            }
        }
    }
}
