using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gisr.Api.Common;
using Gisr.Database.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public sealed class ChainCodeService(ChainCodeRepository profiles, AccountService accounts)
{
    public async Task<ChainCodeProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        return ToDto(await profiles.GetAsync(account.Id, cancellationToken) ?? throw NotFound());
    }

    public async Task<ChainCodeProfileDto> SaveAsync(UpdateChainCodeRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        var normalized = Normalize(request);
        var code = Generate(account.Id, normalized);
        return ToDto(await profiles.UpsertAsync(account.Id, JsonSerializer.Serialize(normalized), code, cancellationToken));
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.EnsureAccountAsync(cancellationToken);
        if (!await profiles.DeleteAsync(account.Id, cancellationToken)) throw NotFound();
    }

    public static UpdateChainCodeRequest Normalize(UpdateChainCodeRequest request)
    {
        var eyes = Colors(request.EyeColor, request.EyeColors);
        var skin = Colors(request.SkinColor, request.SkinColors);
        var hair = Colors(request.HairColor, request.HairColors);
        return request with
        {
            FullName = Text(request.FullName), BirthDate = Text(request.BirthDate),
            Homeworld = Text(request.Homeworld), Species = Text(request.Species), Height = Text(request.Height),
            EyeColor = eyes.Single, EyeColors = eyes.Multiple,
            SkinColor = skin.Single, SkinColors = skin.Multiple,
            HairColor = hair.Single, HairColors = hair.Multiple,
            DistinguishingMarks = Text(request.DistinguishingMarks), FamilyHistory = Text(request.FamilyHistory),
            CriminalHistory = Text(request.CriminalHistory), Biography = Text(request.Biography),
        };
    }

    private static (ProfileColor? Single, ProfileColor[]? Multiple) Colors(ProfileColor? single, ProfileColor[]? multiple)
    {
        var colors = (multiple ?? []).Concat(single.HasValue ? [single.Value] : []).Distinct().Order().ToArray();
        return colors.Length switch
        {
            0 => (null, null),
            1 => (colors[0], null),
            _ => (null, colors),
        };
    }

    public static string Generate(Guid accountId, UpdateChainCodeRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, AccountId = accountId, Profile = Normalize(request) });
        var digest = SHA256.HashData(payload);
        var digits = new BigInteger(digest.AsSpan(0, 16), isUnsigned: true, isBigEndian: true).ToString("D40", CultureInfo.InvariantCulture);
        return string.Join('-', Enumerable.Range(0, 8).Select(index => digits.Substring(index * 5, 5)));
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Normalize(NormalizationForm.FormC);

    private static ChainCodeProfileDto ToDto(ChainCodeRow row) =>
        new(JsonSerializer.Deserialize<UpdateChainCodeRequest>(row.ProfileJson)!, row.ChainCode, row.UpdatedAt);

    private static ProblemException NotFound() => new(StatusCodes.Status404NotFound, "Not Found", "No Chain Code profile has been saved.");
}