using System.Globalization;
using System.Text;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Display-name hygiene for Steam persona / bed-claim names. Steam allows control
/// characters and private-use glyphs (custom Steam fonts) in personas; the browser
/// fonts render those as tofu boxes, so every place a persona is shown routes the
/// name through <see cref="Sanitize"/> first. The stored save data is never touched.
/// </summary>
public static class PersonaNames
{
    /// <summary>
    /// Strips characters a web font cannot render meaningfully: C0/C1 controls,
    /// zero-width/format characters, private-use-area code points (all three PUA
    /// planes), unpaired surrogates and the replacement character. Whitespace runs
    /// collapse to a single space and the result is trimmed.
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var kept = new StringBuilder(name.Length);
        for (var index = 0; index < name.Length; index++)
        {
            int code;
            if (char.IsHighSurrogate(name[index]) && index + 1 < name.Length && char.IsLowSurrogate(name[index + 1]))
            {
                code = char.ConvertToUtf32(name[index], name[index + 1]);
                index++;
            }
            else if (char.IsSurrogate(name[index]))
            {
                continue; // lone surrogate half - never renderable
            }
            else
            {
                code = name[index];
            }
            if (IsRenderable(code)) kept.Append(char.ConvertFromUtf32(code));
        }

        // Collapse whitespace runs left behind by stripped glyph clusters and trim.
        var normalized = new StringBuilder(kept.Length);
        var pendingSpace = false;
        foreach (var ch in kept.ToString())
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = normalized.Length > 0; continue; }
            if (pendingSpace) { normalized.Append(' '); pendingSpace = false; }
            normalized.Append(ch);
        }
        return normalized.ToString();
    }

    private static bool IsRenderable(int code)
    {
        // Controls (C0 + C1) and the replacement character.
        if (code < 0x20 || (code >= 0x7F && code <= 0x9F) || code == 0xFFFD) return false;
        // Private-use areas: BMP PUA and both supplementary PUA planes.
        if (code is >= 0xE000 and <= 0xF8FF) return false;
        if (code is >= 0xF0000 and <= 0xFFFFD) return false;
        if (code is >= 0x100000 and <= 0x10FFFD) return false;
        // Zero-width and directional format characters (incl. BOM/word joiner).
        return CharUnicodeInfo.GetUnicodeCategory(code) != UnicodeCategory.Format;
    }
}
