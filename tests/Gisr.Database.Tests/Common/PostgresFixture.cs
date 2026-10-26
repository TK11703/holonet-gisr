using Gisr.Database.Common;
using Gisr.Database.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gisr.Database.Tests.Common;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        return new TestDatabase(GisrDataSource.Create(builder.ConnectionString));
    }

    public async Task<TestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = await CreateDatabaseAsync();
        var runner = new MigrationRunner(
            database.DataSource, new EmbeddedMigrationSource(), new SqlScriptLoader(), NullLogger<MigrationRunner>.Instance);
        await runner.ApplyAsync(CancellationToken.None);
        return database;
    }
}

public sealed class TestDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var command = DataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
