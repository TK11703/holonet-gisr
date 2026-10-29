using Microsoft.JSInterop;

namespace Gisr.Web.Layout;

// Restyles the page the moment a theme is saved; App.razor sets the attribute for every full page load.
public sealed class ThemeInterop(IJSRuntime js) : IAsyncDisposable, IDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask ApplyAsync(string theme)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
            await _module.InvokeVoidAsync("apply", theme);
        }
        catch (JSDisconnectedException)
        {
            // Circuit is gone; the next page load applies the saved theme.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
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
}
