namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// Vital stats from <c>CharacterStatsSave_Struct</c>. The five survival values are on a 0–100
/// scale in-game. <paramref name="Radiation"/> is the radiation the character has absorbed (0 is
/// none) and <paramref name="BodyTemperature"/> is how far the body is from comfortable (0 is
/// comfortable, negative is cold, positive is hot); both default to 0 and are omitted from the
/// save at that value, like the other stats.
/// </summary>
public sealed record CharacterStats(
    double Hunger,
    double Thirst,
    double Sanity,
    double Fatigue,
    double Continence,
    int Money,
    double Radiation = 0,
    double BodyTemperature = 0);
