using Microsoft.JSInterop;

namespace Needly.Web.Components.Layout;

internal sealed class PwaInstallInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Layout/PwaInstallPrompt.razor.js";
    internal const string InitializeMethod = "initialize";
    internal const string PromptInstallMethod = "promptInstall";
    internal const string DisposeMethod = "dispose";

    private IJSObjectReference? module;

    internal async ValueTask<PwaInstallState> InitializeAsync<T>(DotNetObjectReference<T> dotNetReference)
        where T : class
    {
        module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        return await module.InvokeAsync<PwaInstallState>(InitializeMethod, dotNetReference);
    }

    internal async ValueTask<bool> PromptInstallAsync()
    {
        return module is not null && await module.InvokeAsync<bool>(PromptInstallMethod);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync(DisposeMethod);
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}

internal sealed record PwaInstallState(bool CanInstall, bool ShowIosInstructions);