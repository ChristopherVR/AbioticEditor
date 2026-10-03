using System.Reflection;
using AbioticEditor.Web.Components.World;
using Microsoft.JSInterop;

namespace AbioticEditor.Tests;

public sealed class LiveWorld3DLifecycleTests
{
    [Fact]
    public async Task Returning_to_the_map_stops_layout_observers_and_releases_each_view()
    {
        for (var i = 0; i < 3; i++)
        {
            var tab = new LiveWorld3DTab();
            var fit = new JsHandle("stop");
            var view = new JsHandle("dispose");
            var module = new JsHandle(null);
            Set(tab, "_fit", fit);
            Set(tab, "_view", view);
            Set(tab, "_module", module);
            await tab.DisposeAsync();
            Assert.Equal("stop", Assert.Single(fit.Calls));
            Assert.Equal("dispose", Assert.Single(view.Calls));
            Assert.True(fit.Released);
            Assert.True(view.Released);
            Assert.True(module.Released);
        }
    }

    private static void Set(LiveWorld3DTab tab, string field, JsHandle value)
        => typeof(LiveWorld3DTab).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tab, value);

    private sealed class JsHandle(string? method) : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Released { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
            => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier != method) throw new JSException("The requested function does not exist.");
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() { Released = true; return ValueTask.CompletedTask; }
    }
}
