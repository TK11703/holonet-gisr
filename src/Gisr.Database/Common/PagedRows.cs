namespace Gisr.Database.Common;

public sealed record PagedRows<T>(IReadOnlyList<T> Items, long TotalCount);
