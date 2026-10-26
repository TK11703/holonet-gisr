namespace Gisr.Shared.Common;

public sealed record PagedResult<T>(int Page, int PageSize, long TotalCount, IReadOnlyList<T> Items);
