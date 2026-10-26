using System.Security.Cryptography;
using System.Text;
using Dapper;
using Gisr.Database.Common;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Gisr.Database.Migrations;

public sealed class MigrationRunner(
    NpgsqlDataSource dataSource,
    IMigrationSource source,
    ISqlScriptLoader sql,
    ILogger<MigrationRunner> logger)
{
    private sealed record AppliedRow(string ScriptId, string ChecksumSha256);

    public static string ComputeChecksum(string script)
    {
        var normalized = script.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql.Get("Migrations", "acquire-lock"), cancellationToken: cancellationToken));
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql.Get("Migrations", "bootstrap-ledger"), cancellationToken: cancellationToken));

            var applied = (await connection.QueryAsync<AppliedRow>(
                    new CommandDefinition(sql.Get("Migrations", "get-applied"), cancellationToken: cancellationToken)))
                .ToDictionary(row => row.ScriptId, row => row.ChecksumSha256, StringComparer.Ordinal);

            foreach (var script in source.GetScripts().OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                var checksum = ComputeChecksum(script.Sql);
                if (applied.TryGetValue(script.Id, out var existing))
                {
                    if (!string.Equals(existing, checksum, StringComparison.Ordinal))
                    {
                        throw new MigrationChecksumException(script.Id);
                    }

                    continue;
                }

                await ApplyScriptAsync(connection, script, checksum, cancellationToken);
                logger.LogInformation("Applied migration {ScriptId}", script.Id);
            }

            // Types such as pgvector's `vector` may have been created just now; pooled connections must learn about them.
            await connection.ReloadTypesAsync(cancellationToken);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(sql.Get("Migrations", "release-lock")));
        }
    }

    private async Task ApplyScriptAsync(NpgsqlConnection connection, MigrationScript script, string checksum, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(script.Sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                sql.Get("Migrations", "insert-applied"),
                new { ScriptId = script.Id, ChecksumSha256 = checksum },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
