using Dapper;
using Gisr.Database.Common;
using Npgsql;

namespace Gisr.Database.Search;

public sealed class OcrUsageRepository(NpgsqlDataSource dataSource, ISqlScriptLoader sql)
{
    private const string Feature = "Search";

    // Pages are reserved before the OCR call and not refunded if it fails, so spend can never exceed the budget.
    public async Task<bool> TryReservePagesAsync(DateOnly month, int pages, int budget, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command("ensure-ocr-month", new { Month = month }, null, cancellationToken));
        return await connection.ExecuteAsync(Command("reserve-ocr-pages", new { Month = month, Pages = pages, Budget = budget }, null, cancellationToken)) > 0;
    }

    public async Task<int> GetPagesUsedAsync(DateOnly month, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Command("get-ocr-pages-used", new { Month = month }, null, cancellationToken));
    }

    // Returns how many artifacts were re-queued; only the first call for a month does any work.
    public async Task<int> RequeueBudgetSkippedAsync(DateOnly month, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(Command("ensure-ocr-month", new { Month = month }, transaction, cancellationToken));
        var claimed = await connection.ExecuteAsync(Command("claim-requeue-month", new { Month = month }, transaction, cancellationToken));
        var requeued = claimed == 0 ? 0 : await connection.ExecuteAsync(Command("requeue-budget-skipped", null, transaction, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return requeued;
    }

    private CommandDefinition Command(string script, object? parameters, NpgsqlTransaction? transaction, CancellationToken cancellationToken) =>
        new(sql.Get(Feature, script), parameters, transaction, cancellationToken: cancellationToken);
}
