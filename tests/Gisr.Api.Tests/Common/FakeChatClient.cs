using Microsoft.Extensions.AI;

namespace Gisr.Api.Tests.Common;

// The language model is non-deterministic and costs money, so only it is faked.
public sealed class FakeChatClient : IChatClient
{
    private readonly List<IReadOnlyList<ChatMessage>> _requests = [];

    public Func<string, string> Reply { get; set; } = draft => "Improved: " + draft;

    public Exception? Failure { get; set; }

    public IReadOnlyList<IReadOnlyList<ChatMessage>> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        var sent = messages.ToList();
        lock (_requests)
        {
            _requests.Add(sent);
        }

        var user = sent.Last(m => m.Role == ChatRole.User).Text;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply(user))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
