using Gisr.Database.Common;

namespace Gisr.Database.Tests.Common;

public class SqlScriptLoaderTests
{
    [Fact]
    public void Get_ReturnsEmbeddedScriptText()
    {
        var loader = new SqlScriptLoader();

        var sql = loader.Get("Migrations", "0001_create_extensions");

        Assert.Contains("THIS TEXT IS NOT THERE", sql, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void EverySqlScript_LivesUnderScripts_InItsFeatureFolder()
    {
        var assembly = typeof(SqlScriptLoader).Assembly;
        var prefix = $"{assembly.GetName().Name}.Scripts.";

        var outside = assembly.GetManifestResourceNames()
            .Where(r => r.EndsWith(".sql", StringComparison.Ordinal) && !r.StartsWith(prefix, StringComparison.Ordinal))
            .Order()
            .ToList();
        var features = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix, StringComparison.Ordinal))
            .Select(r => r[prefix.Length..].Split('.')[0])
            .Distinct()
            .Order()
            .ToList();

        Assert.Empty(outside);
        Assert.Equal(["Accounts", "Artifacts", "Gallery", "Migrations", "Notifications", "Records", "Search"], features);
    }

    [Fact]
    public void TheScriptsFolderOnDisk_HasOneSubfolderPerFeature_AndNoSqlElsewhere()
    {
        var project = Path.Combine(RepoRoot(), "src", "Gisr.Database");

        var stray = Directory.EnumerateFiles(project, "*.sql", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.StartsWith(Path.Combine(project, "Scripts") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        var folders = Directory.GetDirectories(Path.Combine(project, "Scripts")).Select(Path.GetFileName).Order().ToList();

        Assert.Empty(stray);
        Assert.Equal(["Accounts", "Artifacts", "Gallery", "Migrations", "Notifications", "Records", "Search"], folders);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Gisr.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Gisr.slnx was not found above the test output directory.");
    }
}
