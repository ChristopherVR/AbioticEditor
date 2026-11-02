namespace AbioticEditor.Core.GamePass;

/// <summary>
/// The engine-version block a version 4 <c>ABF_SAVE_VERSION</c> bundle carries after its version
/// number (see <see cref="AbfSaveBundle.EnginePreamble"/>), captured verbatim from a real Game Pass
/// world written by engine 5.4.4-1050002 (the 1.5 game). A bundle built from scratch, or an older
/// bundle the player chose to upgrade, is stamped with this so the game reads it as current.
/// </summary>
internal static class AbfLatestEngineHeader
{
    private const string Base64 =
        "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";

    public static byte[] Bytes { get; } = global::System.Convert.FromBase64String(Base64);
}