namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Per-limb health from <c>CharacterHealth_</c> (<c>BodyLimbHealth_Struct</c>).
/// Values are 0–100 doubles in the save.
/// </summary>
public sealed record LimbHealth(
    double Head,
    double Torso,
    double LeftArm,
    double RightArm,
    double LeftLeg,
    double RightLeg)
{
    public static LimbHealth Full { get; } = new(100, 100, 100, 100, 100, 100);
}
