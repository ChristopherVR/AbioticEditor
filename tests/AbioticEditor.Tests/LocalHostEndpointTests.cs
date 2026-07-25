using AbioticEditor.Web.Services;
using System.Reflection;

namespace AbioticEditor.Tests;

public sealed class LocalHostEndpointTests
{
    [Fact]
    public void Desktop_entry_point_is_marked_sta_for_windows_webview()
    {
        var main = typeof(AbioticEditor.Web.Program).GetMethod(nameof(AbioticEditor.Web.Program.Main),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(main);
        Assert.NotNull(main.GetCustomAttribute<STAThreadAttribute>());
        Assert.Equal(typeof(void), main.ReturnType);
    }

    [Theory]
    [InlineData(null, LocalHostEndpoint.DefaultUrl)]
    [InlineData("http://localhost:41000", "http://localhost:41000")]
    [InlineData("http://[::1]:41000", "http://[::1]:41000")]
    public void Resolve_accepts_only_explicit_loopback_urls(string? configuredUrl, string expected)
        => Assert.Equal(expected, LocalHostEndpoint.Resolve(configuredUrl));

    [Theory]
    [InlineData("http://0.0.0.0:37246")]
    [InlineData("http://192.168.1.25:37246")]
    [InlineData("https://127.0.0.1:37246")]
    [InlineData("http://127.0.0.1:80")]
    [InlineData("http://127.0.0.1:37246/admin")]
    public void Resolve_rejects_non_local_or_non_host_urls(string configuredUrl)
        => Assert.Throws<InvalidOperationException>(() => LocalHostEndpoint.Resolve(configuredUrl));

    [Theory]
    [InlineData(false, true, null, null, true)]
    [InlineData(false, true, "0", null, true)]
    [InlineData(false, true, "1", null, false)]
    [InlineData(false, true, "true", null, false)]
    [InlineData(false, false, null, null, false)]
    [InlineData(true, true, null, null, false)]
    [InlineData(true, true, null, "wayland-0", true)]
    public void Packaged_host_opens_a_desktop_window_only_for_interactive_sessions(
        bool isLinux, bool isUserInteractive, string? disabled, string? displayServer, bool expected)
        => Assert.Equal(expected, DesktopWindowHost.ShouldOpen(isLinux, isUserInteractive, disabled, displayServer));
}
