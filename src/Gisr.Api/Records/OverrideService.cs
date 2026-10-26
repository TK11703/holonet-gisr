using System.Security.Cryptography;
using System.Text;
using Gisr.Api.Accounts;
using Gisr.Api.Common;
using Gisr.Database.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Records;

public sealed class OverrideService(
    RecordRepository records,
    ViewGrantRepository grants,
    OverrideAttemptRepository attempts,
    AccountService accounts,
    ICurrentUser user)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    private const int TokenBytes = 32;
    private const int MinTokenLength = 43;
    private const int MaxTokenLength = 64;

    public async Task<UnlockedRecordDto> SubmitAsync(Guid recordId, OverrideCodeRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var caller = new RecordCaller(account.Id, user.IsOfficer);

        // Only the subject of a sealed record can enter its code; everyone else learns nothing.
        var current = await records.GetAsync(recordId, caller, cancellationToken);
        if (current is null || !current.Record.IsSealed || caller.IsOfficer || current.Record.SubjectAccountId != account.Id)
        {
            throw NotFound();
        }

        var lockout = await attempts.GetLockoutAsync(recordId, account.Id, LockoutWindow, cancellationToken);
        if (lockout.Failures >= MaxFailures)
        {
            throw new ProblemException(
                StatusCodes.Status429TooManyRequests,
                "Too Many Requests",
                "Too many incorrect override codes. Try again later.",
                headers: new Dictionary<string, string> { ["Retry-After"] = Math.Max(1, lockout.RetryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        var hash = await records.GetOverrideCodeHashAsync(recordId, account.Id, cancellationToken);
        var correct = hash is not null && OverrideCodeHasher.Verify(request.Code, hash);
        await attempts.InsertAsync(recordId, account.Id, correct, cancellationToken);

        if (!correct)
        {
            throw new ProblemException(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "The override code is incorrect.",
                new Dictionary<string, object?> { ["attemptsRemaining"] = Math.Max(0, MaxFailures - (int)lockout.Failures - 1) });
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = await grants.InsertAsync(recordId, account.Id, HashToken(token), cancellationToken);

        var unlockedCaller = caller with { Unlocked = true };
        var unlocked = await records.GetAsync(recordId, unlockedCaller, cancellationToken) ?? throw NotFound();
        return RecordMapper.ToUnlocked(unlocked, unlockedCaller, token, expiresAt);
    }

    public async Task EndAsync(Guid recordId, string? viewToken, CancellationToken cancellationToken)
    {
        if (!IsPlausible(viewToken))
        {
            return;
        }

        var account = await accounts.EnsureAccountAsync(cancellationToken);
        await grants.RevokeAsync(recordId, account.Id, HashToken(viewToken!), cancellationToken);
    }

    public async Task<bool> IsValidAsync(Guid recordId, string? viewToken, CancellationToken cancellationToken)
    {
        if (!IsPlausible(viewToken))
        {
            return false;
        }

        var account = await accounts.EnsureAccountAsync(cancellationToken);
        return await grants.IsValidAsync(recordId, account.Id, HashToken(viewToken!), cancellationToken);
    }

    private static bool IsPlausible(string? viewToken) =>
        viewToken is { Length: >= MinTokenLength and <= MaxTokenLength };

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ProblemException NotFound() => new(StatusCodes.Status404NotFound, "Not Found");
}
