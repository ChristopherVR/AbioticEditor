using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class LocalRequestGuardTests
{
    [Theory]
    [InlineData("127.0.0.1:37246", null, true)]
    [InlineData("localhost:37246", "http://localhost:37246", true)]
    [InlineData("127.0.0.1:37246", "http://127.0.0.1:37246", true)]
    [InlineData("evil.example:37246", null, false)]
    [InlineData("127.0.0.1:37246", "https://evil.example", false)]
    [InlineData("127.0.0.1:80", null, false)]
    [InlineData(null, null, false)]
    public void Only_the_editors_own_address_is_allowed(string? host, string? origin, bool expected)
        => Assert.Equal(expected, LocalRequestGuard.IsAllowed(host, origin, 37246));
}
