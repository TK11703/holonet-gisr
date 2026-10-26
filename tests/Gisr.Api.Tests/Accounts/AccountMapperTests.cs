using System.Reflection;
using Gisr.Api.Accounts;
using Gisr.Database.Accounts;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Tests.Accounts;

public class AccountMapperTests
{
    private static AccountRow Row() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        EntraObjectId = Guid.NewGuid(),
        DisplayName = "Leia Organa",
        SignInEmail = "leia@alderaan.example",
        ContactEmail = "contact@alderaan.example",
        NotifyOnServiceRecord = false,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        LastSignInAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void ToDto_MapsRowFields_AndOfficerFlag()
    {
        var row = Row();

        var dto = AccountMapper.ToDto(row, isOfficer: true);

        Assert.Equal(row.Id, dto.Id);
        Assert.Equal("Leia Organa", dto.DisplayName);
        Assert.Equal("leia@alderaan.example", dto.SignInEmail);
        Assert.Equal("contact@alderaan.example", dto.ContactEmail);
        Assert.False(dto.NotifyOnServiceRecord);
        Assert.True(dto.IsOfficer);
        Assert.Equal(row.CreatedAt, dto.CreatedAt);
    }

    [Fact]
    public void AccountDto_NeverExposesTenantOrObjectIds()
    {
        var names = typeof(AccountDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Tenant", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("ObjectId", StringComparison.OrdinalIgnoreCase));
    }
}
