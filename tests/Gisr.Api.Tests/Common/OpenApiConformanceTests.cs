using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Gisr.Api.Tests.Common;

[Collection("api")]
public class OpenApiConformanceTests(PostgresFixture postgres)
{
    private static readonly string[] Methods = ["get", "put", "post", "patch", "delete"];

    // 401 comes from the authentication middleware, not from endpoint metadata.
    private static readonly string[] ImplicitStatuses = ["401"];

    private sealed record Operation(string Key, SortedSet<string> Statuses, SortedSet<string> Parameters, bool HasBody);

    private static readonly Lazy<(YamlMappingNode Root, string Path)> Contract = new(LoadContract);

    private static (YamlMappingNode, string) LoadContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "specs", "001-gisr-core-records", "contracts", "api.openapi.yaml");
            if (File.Exists(candidate))
            {
                var stream = new YamlStream();
                using var reader = new StreamReader(candidate);
                stream.Load(reader);
                return ((YamlMappingNode)stream.Documents[0].RootNode, candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("specs/001-gisr-core-records/contracts/api.openapi.yaml was not found above the test output directory.");
    }

    private static YamlNode? Get(YamlNode? node, string key) =>
        node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Text(YamlNode? node) => (node as YamlScalarNode)?.Value;

    private static IEnumerable<KeyValuePair<string, YamlNode>> Entries(YamlNode? node) =>
        node is YamlMappingNode map ? map.Children.Select(c => KeyValuePair.Create(Text(c.Key)!, c.Value)) : [];

    private static YamlNode Resolve(YamlNode node)
    {
        var reference = Text(Get(node, "$ref"));
        if (reference is null)
        {
            return node;
        }

        var target = Contract.Value.Root;
        YamlNode? current = target;
        foreach (var part in reference.TrimStart('#', '/').Split('/'))
        {
            current = Get(current, part);
        }

        return current ?? throw new InvalidOperationException($"Unresolved contract reference {reference}.");
    }

    private static IEnumerable<string> ParameterKeys(YamlNode? parameters) =>
        parameters is YamlSequenceNode sequence
            ? sequence.Children.Select(Resolve).Select(p => $"{Text(Get(p, "in"))}:{Text(Get(p, "name"))}".ToLowerInvariant())
            : [];

    private static Dictionary<string, Operation> ContractOperations()
    {
        var operations = new Dictionary<string, Operation>();
        foreach (var (path, item) in Entries(Get(Contract.Value.Root, "paths")))
        {
            var shared = ParameterKeys(Get(item, "parameters")).ToList();
            foreach (var method in Methods)
            {
                if (Get(item, method) is not { } operation)
                {
                    continue;
                }

                var key = $"{method.ToUpperInvariant()} {path}";
                operations[key] = new Operation(
                    key,
                    [.. Entries(Get(operation, "responses")).Select(r => r.Key)],
                    [.. shared.Concat(ParameterKeys(Get(operation, "parameters")))],
                    Get(operation, "requestBody") is not null);
            }
        }

        return operations;
    }

    private static Dictionary<string, Operation> GeneratedOperations(JsonElement document)
    {
        var operations = new Dictionary<string, Operation>();
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(m => Methods.Contains(m.Name)))
            {
                var key = $"{method.Name.ToUpperInvariant()} {path.Name}";
                var parameters = method.Value.TryGetProperty("parameters", out var list)
                    ? list.EnumerateArray().Select(p => $"{p.GetProperty("in").GetString()}:{p.GetProperty("name").GetString()}".ToLowerInvariant())
                    : [];
                operations[key] = new Operation(
                    key,
                    [.. method.Value.GetProperty("responses").EnumerateObject().Select(r => r.Name)],
                    [.. parameters],
                    method.Value.TryGetProperty("requestBody", out _));
            }
        }

        return operations;
    }

    private async Task<JsonElement> FetchGeneratedDocumentAsync()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var json = await factory.CreateUserClient().GetStringAsync("/openapi/v1.json");
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task EveryContractOperation_Exists_AndEveryImplementedOperation_IsInTheContract()
    {
        var generated = GeneratedOperations(await FetchGeneratedDocumentAsync());
        var contract = ContractOperations();

        Assert.Empty(contract.Keys.Except(generated.Keys).Order());
        Assert.Empty(generated.Keys.Except(contract.Keys).Order());
    }

    [Fact]
    public async Task EveryOperation_DeclaresTheContractStatusCodes()
    {
        var generated = GeneratedOperations(await FetchGeneratedDocumentAsync());
        var differences = new List<string>();
        foreach (var expected in ContractOperations().Values)
        {
            if (!generated.TryGetValue(expected.Key, out var actual))
            {
                continue;
            }

            var wanted = expected.Statuses.Except(ImplicitStatuses).ToList();
            var missing = wanted.Except(actual.Statuses).ToList();
            var extra = actual.Statuses.Except(expected.Statuses).Except(ImplicitStatuses).ToList();
            if (missing.Count > 0 || extra.Count > 0)
            {
                differences.Add($"{expected.Key}: missing [{string.Join(", ", missing)}], undocumented [{string.Join(", ", extra)}]");
            }
        }

        Assert.True(differences.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public async Task EveryOperation_TakesTheContractPathQueryParametersAndBody()
    {
        var generated = GeneratedOperations(await FetchGeneratedDocumentAsync());
        var differences = new List<string>();
        foreach (var expected in ContractOperations().Values)
        {
            if (!generated.TryGetValue(expected.Key, out var actual))
            {
                continue;
            }

            // The view token is a header read by the handler, not always modelled as a bound parameter.
            var wanted = expected.Parameters.Where(p => !p.StartsWith("header:", StringComparison.Ordinal)).ToList();
            var got = actual.Parameters.Where(p => !p.StartsWith("header:", StringComparison.Ordinal)).ToList();
            if (!wanted.Order().SequenceEqual(got.Order()))
            {
                differences.Add($"{expected.Key}: contract [{string.Join(", ", wanted.Order())}] vs implemented [{string.Join(", ", got.Order())}]");
            }

            if (expected.HasBody != actual.HasBody)
            {
                differences.Add($"{expected.Key}: request body contract={expected.HasBody} implemented={actual.HasBody}");
            }
        }

        Assert.True(differences.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public async Task SchemasSharingANameWithTheContract_HaveTheSameProperties()
    {
        var document = await FetchGeneratedDocumentAsync();
        var generatedSchemas = document.GetProperty("components").GetProperty("schemas");
        var differences = new List<string>();
        var compared = 0;
        foreach (var (name, schema) in Entries(Get(Get(Contract.Value.Root, "components"), "schemas")))
        {
            if (Get(schema, "properties") is not { } properties || !generatedSchemas.TryGetProperty(name, out var actual))
            {
                continue;
            }

            compared++;
            var wanted = Entries(properties).Select(p => p.Key).ToList();
            var got = actual.TryGetProperty("properties", out var props) ? props.EnumerateObject().Select(p => p.Name).ToList() : [];
            var missing = wanted.Except(got, StringComparer.OrdinalIgnoreCase).ToList();
            var extra = got.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0 || extra.Count > 0)
            {
                differences.Add($"{name}: missing [{string.Join(", ", missing)}], undocumented [{string.Join(", ", extra)}]");
            }
        }

        Assert.True(compared >= 10, $"Only {compared} schemas could be matched by name; the comparison is not meaningful.");
        Assert.True(differences.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, differences));
    }
}
