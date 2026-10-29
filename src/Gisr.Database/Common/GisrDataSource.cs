using System.Data;
using Azure.Core;
using Dapper;
using Npgsql;
using Pgvector.Dapper;

namespace Gisr.Database.Common;

public static class GisrDataSource
{
    public const string EntraScope = "https://ossrdbms-aad.database.windows.net/.default";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Pass a credential to authenticate with Microsoft Entra tokens instead of a password in the connection string.</summary>
    public static NpgsqlDataSource Create(string connectionString, TokenCredential? credential = null)
    {
        RegisterTypeHandlers();
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        if (credential is not null)
        {
            builder.UsePeriodicPasswordProvider(
                async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext([EntraScope]), ct)).Token,
                TimeSpan.FromMinutes(45),
                TimeSpan.FromSeconds(10));
        }

        return builder.Build();
    }

    public static void RegisterTypeHandlers()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new DateOnlyHandler());
            SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
            SqlMapper.AddTypeHandler(new VectorTypeHandler());
            _registered = true;
        }
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value;
        }

        public override DateOnly Parse(object value) =>
            value switch
            {
                DateOnly date => date,
                DateTime dateTime => DateOnly.FromDateTime(dateTime),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly."),
            };
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.DateTime;
            parameter.Value = value.UtcDateTime;
        }

        public override DateTimeOffset Parse(object value) =>
            value switch
            {
                DateTimeOffset offset => offset.ToUniversalTime(),
                DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateTimeOffset."),
            };
    }
}
