using AbioticEditor.Updater;

namespace AbioticEditor.Tests;

public sealed class UpdaterIntegrityTests
{
    [Theory]
    [InlineData("https://github.com/o/r/releases/download/v1/a.zip", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("http://github.com/o/r/a.zip", false)]
    [InlineData("https://github.com.evil.example/a.zip", false)]
    [InlineData("https://evil.example/a.zip", false)]
    public void Only_https_github_hosts_are_trusted(string url, bool trusted)
    {
        if (trusted) UpdateInstaller.EnsureTrustedDownloadUrl(url);
        else Assert.Throws<UpdaterException>(() => UpdateInstaller.EnsureTrustedDownloadUrl(url));
    }

    [Fact]
    public void Digest_parsing_accepts_only_sha256_hex()
    {
        var hex = new string('A', 64);
        Assert.Equal(hex.ToLowerInvariant(), GitHubReleaseClient.ParseSha256Digest("sha256:" + hex));
        Assert.Null(GitHubReleaseClient.ParseSha256Digest("sha1:abc"));
        Assert.Null(GitHubReleaseClient.ParseSha256Digest("sha256:zz"));
        Assert.Null(GitHubReleaseClient.ParseSha256Digest(null));
    }
}
