using Microsoft.JSInterop;

namespace Gisr.Web.Layout;

public sealed class ShellInterop(IJSRuntime js) : IAsyncDisposable, IDisposable
{
    private IJSObjectReference? _focusTrap;
    private IJSObjectReference? _shortcuts;
    private bool _shortcutRegistered;

    public async ValueTask RegisterSearchShortcutAsync(Microsoft.AspNetCore.Components.ElementReference input)
    {
        _shortcuts ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/shortcuts.js");
        await _shortcuts.InvokeVoidAsync("register", input);
        _shortcutRegistered = true;
    }

    public async ValueTask<IAsyncDisposable> TrapFocusAsync(Microsoft.AspNetCore.Components.ElementReference container, Func<Task> onEscape)
    {
        _focusTrap ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/focus-trap.js");
        var callback = new EscapeCallback(onEscape);
        var reference = DotNetObjectReference.Create(callback);
        var id = await _focusTrap.InvokeAsync<int>("trap", container, reference);
        return new FocusTrapHandle(_focusTrap, reference, id);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_shortcuts is not null)
            {
                if (_shortcutRegistered)
                {
                    await _shortcuts.InvokeVoidAsync("unregister");
                }

                await _shortcuts.DisposeAsync();
            }

            if (_focusTrap is not null)
            {
                await _focusTrap.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit is gone; nothing left to clean up in the browser.
        }
    }

    // Sync containers (such as bUnit) call Dispose; real circuits dispose through DisposeAsync above.
    public void Dispose()
    {
    }

    public sealed class EscapeCallback(Func<Task> onEscape)
    {
        [JSInvokable]
        public Task OnEscape() => onEscape();
    }

    private sealed class FocusTrapHandle(IJSObjectReference module, DotNetObjectReference<EscapeCallback> reference, int id) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await module.InvokeVoidAsync("release", id);
            }
            catch (JSDisconnectedException)
            {
                // Circuit is gone.
            }
            finally
            {
                reference.Dispose();
            }
        }
    }
}
