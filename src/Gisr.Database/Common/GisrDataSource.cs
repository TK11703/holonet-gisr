using System.Data;
using Dapper;
using Npgsql;
using Pgvector.Dapper;

namespace Gisr.Database.Common;

public static class GisrDataSource
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static NpgsqlDataSource Create(string connectionString)
    {
        RegisterTypeHandlers();
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
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
