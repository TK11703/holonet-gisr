using Bunit;
using Gisr.Web.Accounts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Accounts;

public class HomePageTests : BunitContext
{
    [Fact]
    public void Home_RedirectsToTheRecordList()
    {
        Render<Home>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/records", navigation.Uri, StringComparison.Ordinal);
    }
}
