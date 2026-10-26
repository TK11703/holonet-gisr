using Serilog.Core;
using Serilog.Events;

namespace Gisr.Api.Tests.Common;

public sealed class CapturingLogSink : ILogEventSink
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        // Rendered text plus every property value, so nothing structured can hide a secret.
        var text = logEvent.RenderMessage() + " " + string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"))
                   + " " + logEvent.Exception;
        lock (_messages)
        {
            _messages.Add(text);
        }
    }
}
