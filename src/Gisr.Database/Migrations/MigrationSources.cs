using System.Reflection;

namespace Gisr.Database.Migrations;

public sealed record MigrationScript(string Id, string Sql);

public interface IMigrationSource
{
    IReadOnlyList<MigrationScript> GetScripts();
}

public sealed class EmbeddedMigrationSource(Assembly assembly) : IMigrationSource
{
    public EmbeddedMigrationSource() : this(typeof(EmbeddedMigrationSource).Assembly)
    {
    }

    public IReadOnlyList<MigrationScript> GetScripts()
    {
        var prefix = $"{assembly.GetName().Name}.Migrations.";
        var scripts = new List<MigrationScript>();

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal) || !resource.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var id = resource[prefix.Length..^".sql".Length];
            if (id.Length < 5 || !char.IsAsciiDigit(id[0]) || id[4] != '_')
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            scripts.Add(new MigrationScript(id, reader.ReadToEnd()));
        }

        return [.. scripts.OrderBy(s => s.Id, StringComparer.Ordinal)];
    }
}
