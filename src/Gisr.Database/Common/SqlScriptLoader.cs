using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Gisr.Database.Common;

public interface ISqlScriptLoader
{
    string Get(string feature, string name);
}

public sealed class SqlScriptLoader(Assembly assembly) : ISqlScriptLoader
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public SqlScriptLoader() : this(typeof(SqlScriptLoader).Assembly)
    {
    }

    public string Get(string feature, string name) =>
        _cache.GetOrAdd($"{feature}.{name}", Load);

    private string Load(string key)
    {
        var resource = $"{assembly.GetName().Name}.{key}.sql";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"SQL script '{key}' was not found as an embedded resource.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
