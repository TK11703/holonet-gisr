using Bunit;
using Gisr.Web.Common;

namespace Gisr.Web.Tests.Common;

public class NotFoundTests : BunitContext
{
    [Fact]
    public void ShowsOneTitle_TheErrorCode_AndTheMessage()
    {
        var cut = Render<NotFound>();

        Assert.Single(cut.FindAll("h1"));
        Assert.Contains("Oops!", cut.Find("h1").TextContent, StringComparison.Ordinal);
        Assert.Contains("Error 404", cut.Find("h1").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find(".error-code").GetAttribute("aria-hidden"));
        Assert.Equal("We can't seem to find the page you're looking for", cut.Find("h2").TextContent.Trim());
    }

    [Fact]
    public void OffersOnlyBackAndHome()
    {
        var cut = Render<NotFound>();

        Assert.Equal(["Back", "Home"], cut.FindAll(".error-links .btn").Select(b => b.TextContent.Trim()));
        Assert.Equal("/", cut.Find(".error-links a").GetAttribute("href"));
    }

    [Fact]
    public void Back_GoesBackInTheBrowserHistory()
    {
        var cut = Render<NotFound>();

        cut.Find("#not-found-back").Click();

        JSInterop.VerifyInvoke("history.back");
    }
}
