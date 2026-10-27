using Gisr.Api.Records;
using Gisr.Database.Records;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

public class SealedRecordMapperTests
{
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid Officer = Guid.NewGuid();

    private static RecordRow Row(bool sealedRecord = true, bool locked = true) => new()
    {
        Id = Guid.NewGuid(),
        SubjectAccountId = Subject,
        AuthorAccountId = Officer,
        Title = locked ? string.Empty : "Secret commendation",
        Affiliation = locked ? string.Empty : "military",
        EngagementStart = locked ? null : new DateOnly(2019, 1, 1),
        EngagementEnd = locked ? null : new DateOnly(2020, 1, 1),
        EngagementDetail = locked ? "{}" : """{"summary":"x"}""",
        SubjectDisplayName = locked ? string.Empty : "Citizen",
        AuthorDisplayName = locked ? string.Empty : "Officer",
        IsSealed = sealedRecord,
        IsLocked = locked,
        Version = 3,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void LockedSummary_ExposesOnlyIdFlagsAndCreationDate()
    {
        var row = Row();

        var dto = RecordMapper.ToSummary(row);

        Assert.Equal(row.Id, dto.Id);
        Assert.True(dto.IsLocked);
        Assert.True(dto.IsSealed);
        Assert.Equal(row.CreatedAt, dto.CreatedAt);
        Assert.Null(dto.Title);
        Assert.Null(dto.Affiliation);
        Assert.Null(dto.SubjectAccountId);
        Assert.Null(dto.SubjectDisplayName);
        Assert.Null(dto.AuthorDisplayName);
        Assert.Null(dto.EngagementStart);
        Assert.Null(dto.EngagementEnd);
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public void UnlockedSealedSummary_ShowsTheContent_ForOfficers()
    {
        var dto = RecordMapper.ToSummary(Row(locked: false));

        Assert.False(dto.IsLocked);
        Assert.True(dto.IsSealed);
        Assert.Equal("Secret commendation", dto.Title);
        Assert.Equal("Citizen", dto.SubjectDisplayName);
        Assert.NotNull(dto.UpdatedAt);
    }

    [Fact]
    public void OrdinarySummary_IsNotSealedOrLocked()
    {
        var dto = RecordMapper.ToSummary(Row(sealedRecord: false, locked: false));

        Assert.False(dto.IsSealed);
        Assert.False(dto.IsLocked);
    }

    [Fact]
    public void Dto_CarriesTheSealedFlag()
    {
        var aggregate = new RecordAggregate(Row(locked: false), [], []);

        Assert.True(RecordMapper.ToDto(aggregate, new RecordCaller(Officer, true)).IsSealed);
        Assert.False(RecordMapper.ToDto(new RecordAggregate(Row(false, false), [], []), new RecordCaller(Officer, true)).IsSealed);
    }

    [Fact]
    public void UnlockedRecordDto_CombinesRecordTokenAndExpiry_WithoutEditRights()
    {
        var aggregate = new RecordAggregate(Row(locked: false), [], []);
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);

        var dto = RecordMapper.ToUnlocked(aggregate, new RecordCaller(Subject, false, true), "view-token-value", expires);

        Assert.Equal("view-token-value", dto.ViewToken);
        Assert.Equal(expires, dto.ExpiresAt);
        Assert.Equal("Secret commendation", dto.Record.Title);
        Assert.True(dto.Record.IsSealed);
        Assert.False(dto.Record.CanEdit);
    }
}
