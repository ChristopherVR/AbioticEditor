using System.IO;
using UeSaveGame;

namespace AbioticEditor.Tests;

public class RoundTripTests
{
    public static IEnumerable<object[]> AllSaves()
    {
        var root = Fixtures.CascadeDir;
        if (root is null)
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.sav", SearchOption.AllDirectories))
        {
            yield return new object[] { Path.GetRelativePath(root, path) };
        }
    }

    [Theory]
    [MemberData(nameof(AllSaves))]
    public void RoundTrip_ProducesIdenticalBytes(string relativePath)
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var fullPath = Path.Combine(Fixtures.CascadeDir!, relativePath);
        var original = File.ReadAllBytes(fullPath);

        SaveGame save;
        using (var input = new MemoryStream(original))
        {
            save = SaveGame.LoadFrom(input);
        }

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            save.WriteTo(output);
            rewritten = output.ToArray();
        }

        if (!original.AsSpan().SequenceEqual(rewritten))
        {
            var firstDiff = FirstDifference(original, rewritten);
            Assert.Fail(
                $"Round-trip mismatch for {relativePath}: " +
                $"original={original.Length}B, rewritten={rewritten.Length}B, " +
                $"first diff at offset 0x{firstDiff:X}");
        }
    }

    private static long FirstDifference(byte[] a, byte[] b)
    {
        var min = Math.Min(a.Length, b.Length);
        for (var i = 0; i < min; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }
        return min;
    }
}
