using Gisr.Database.Common;

namespace Gisr.Database.Tests.Common;

public class SqlScriptLoaderTests
{
    [Fact]
    public void Get_ReturnsEmbeddedScriptText()
    {
        var loader = new SqlScriptLoader();

        var sql = loader.Get("Migrations", "0001_create_extensions");

        Assert.Contains("CREATE EXTENSION", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_CachesScriptsAfterFirstLoad()
    {
        var loader = new SqlScriptLoader();

        var first = loader.Get("Migrations", "0001_create_extensions");
        var second = loader.Get("Migrations", "0001_create_extensions");

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_Throws_ForUnknownScript()
    {
        var loader = new SqlScriptLoader();

        var ex = Assert.Throws<InvalidOperationException>(() => loader.Get("Migrations", "does-not-exist"));

        Assert.Contains("does-not-exist", ex.Message, StringComparison.Ordinal);
    }
}
