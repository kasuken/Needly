using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Needly.Web.Components.Inbox;

internal sealed class InboxKeyboardInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string ImportMethod = "import";
    private const string ModulePath = "./Components/Pages/Inbox.razor.js";
    private const string CreateMethod = "createInboxKeyboardNavigator";
    private const string DisposeMethod = "dispose";
    private IJSObjectReference? _module;
    private IJSObjectReference? _navigator;

    internal async ValueTask InitializeAsync(
        ElementReference root,
        DotNetObjectReference<global::Needly.Web.Components.Pages.Inbox> receiver)
    {
        _module = await jsRuntime.InvokeAsync<IJSObjectReference>(ImportMethod, ModulePath);
        _navigator = await _module.InvokeAsync<IJSObjectReference>(CreateMethod, root, receiver);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_navigator is not null)
            {
                await _navigator.InvokeVoidAsync(DisposeMethod);
                await _navigator.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}