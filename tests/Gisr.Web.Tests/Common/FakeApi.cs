using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gisr.Web.Common;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Web.Tests.Common;

public sealed class FakeApi : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<(HttpMethod Method, string Path, Func<Call, HttpResponseMessage> Respond)> _routes = [];

    public sealed record Call(HttpMethod Method, string PathAndQuery, string? Body, IReadOnlyDictionary<string, string> Headers);

    private readonly List<Call> _calls = [];

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public FakeApi On(HttpMethod method, string path, Func<Call, HttpResponseMessage> respond)
    {
        _routes.Add((method, path.TrimStart('/'), respond));
        return this;
    }

    public FakeApi OnJson(HttpMethod method, string path, object? body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => JsonResponse(status, body));

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, object? body) =>
        new(status) { Content = JsonContent.Create(body, options: Json) };

    public static HttpResponseMessage Problem(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/problem+json") };

    public ApiClient CreateClient() => new(new HttpClient(this) { BaseAddress = new Uri("https://api.test/") }, new FakeTokens());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery.TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var call = new Call(request.Method, path, body, headers);
        lock (_calls)
        {
            _calls.Add(call);
        }

        var route = _routes.LastOrDefault(r => r.Method == request.Method && r.Path == path);
        return route.Respond is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : route.Respond(call);
    }

    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("test-token");
    }

    private sealed class FakeAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => new("test-antiforgery-token", "__RequestVerificationToken");
    }

    public IServiceCollection AddTo(IServiceCollection services)
    {
        var client = CreateClient();
        services.AddSingleton(client);
        services.AddScoped<CurrentUserState>();
        services.AddSingleton<Gisr.Web.Records.ViewGrantStore>();
        services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgery>();
        return services;
    }
}
