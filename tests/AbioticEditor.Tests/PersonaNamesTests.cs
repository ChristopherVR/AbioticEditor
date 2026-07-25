namespace AbioticEditor.Tests;

using AbioticEditor.Web.Services;

/// <summary>
/// Persona display names may carry Steam's control / private-use glyphs that browser
/// fonts render as tofu boxes; the sanitizer strips exactly those and nothing else.
/// </summary>
public sealed class PersonaNamesTests
{
    [Theory]
    [InlineData("Tribbes", "Tribbes")]
    [InlineData("Tribbes", "Tribbes")] // BMP private-use wrappers (the fixture's tofu boxes)
    [InlineData("J0K3R", "J0K3R")] // C0 + C1 controls
    [InlineData("Man​tis", "Mantis")] // zero-width space
    [InlineData("﻿Mantis", "Mantis")] // BOM / zero-width no-break space
    [InlineData("a�b", "ab")] // replacement character
    [InlineData("  spaced   name  ", "spaced name")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitize_strips_unrenderable_characters(string? raw, string expected)
        => Assert.Equal(expected, PersonaNames.Sanitize(raw));

    [Fact]
    public void Sanitize_keeps_international_text_and_symbols()
    {
        Assert.Equal("Ægir Ω 東京 nörd", PersonaNames.Sanitize("Ægir Ω 東京 nörd"));
        Assert.Equal("héro & <tag>", PersonaNames.Sanitize("héro & <tag>"));
    }

    [Fact]
    public void Sanitize_drops_supplementary_private_use_planes_but_keeps_emoji()
    {
        // U+F0000 (plane 15 PUA) is encoded as a surrogate pair; emoji are not PUA.
        var pua = char.ConvertFromUtf32(0xF0000);
        Assert.Equal("x", PersonaNames.Sanitize($"x{pua}"));
        Assert.Equal("🙂", PersonaNames.Sanitize("🙂"));
    }
}
