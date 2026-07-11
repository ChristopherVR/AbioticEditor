namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Vital stats from <c>CharacterStatsSave_Struct</c>. Each value is on a 0–100 scale in-game.
/// </summary>
public sealed record CharacterStats(
    double Hunger,
    double Thirst,
    double Sanity,
    double Fatigue,
    double Continence,
    int Money);
