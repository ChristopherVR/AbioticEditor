namespace AbioticEditor.Web.Models;

/// <summary>Editable player survival and limb-health values, independent of any UI host.</summary>
public sealed class PlayerVitals
{
    public double Hunger { get; set; }
    public double Thirst { get; set; }
    public double Sanity { get; set; }
    public double Fatigue { get; set; }
    public double Continence { get; set; }
    public double Money { get; set; }
    public double Head { get; set; }
    public double Torso { get; set; }
    public double LeftArm { get; set; }
    public double RightArm { get; set; }
    public double LeftLeg { get; set; }
    public double RightLeg { get; set; }
    /// <summary>Absorbed radiation; 0 is none.</summary>
    public double Radiation { get; set; }
    /// <summary>Body temperature on the game's own scale; <see cref="NormalBodyTemperature"/> is comfortable.</summary>
    public double BodyTemperature { get; set; }
    /// <summary>The comfortable temperature: 0 in a save file, the character's own preferred value live.</summary>
    public double NormalBodyTemperature { get; set; }
    /// <summary>False when the source cannot report radiation and temperature (an older live agent),
    /// so the tab hides them instead of offering values it cannot write.</summary>
    public bool HasEnvironmentStats { get; set; } = true;
    public PlayerVitals Clone() => (PlayerVitals)MemberwiseClone();
    public void HealAll(double maximum = 100) => Head = Torso = LeftArm = RightArm = LeftLeg = RightLeg = maximum;
}

/// <summary>Host-neutral boundary for an open player-vitals editing session.</summary>
public interface IPlayerVitalsSession
{
    PlayerVitals Vitals { get; }
    bool IsDirty { get; }
    string? Status { get; }
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    void Revert();
}
