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
