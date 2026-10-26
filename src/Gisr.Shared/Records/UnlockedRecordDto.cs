namespace Gisr.Shared.Records;

// ViewToken is for the Web server only and must never be forwarded to the browser.
public sealed record UnlockedRecordDto(RecordDto Record, string ViewToken, DateTimeOffset ExpiresAt);
