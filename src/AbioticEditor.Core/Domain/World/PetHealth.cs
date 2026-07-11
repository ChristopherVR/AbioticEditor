namespace AbioticEditor.Core.WorldSaves;

/// <summary>A pet's life status, derived from <see cref="WorldPet.IsDead"/> and limb health.</summary>
public enum PetStatus
{
    /// <summary>All tracked limbs at full (no limb below the pet's strongest).</summary>
    Healthy,

    /// <summary>Alive, but at least one tracked limb is damaged.</summary>
    Hurt,

    /// <summary>Alive but all limb health is gone - the game's "critically wounded" / downed state.</summary>
    Downed,

    /// <summary>The persisted <c>IsDead</c> flag is set.</summary>
    Dead,
}

/// <summary>
/// Shared pet health logic for the CLI and App. The save's <c>CurrentHealthMap_</c> records
/// only the <em>current</em> per-limb value - the blueprint maximum is not in the save - so
/// "full" is treated as the pet's strongest current limb. Creatures only use a subset of
/// limbs (e.g. a pest tracks the head; its other limbs sit at 0), so edits only touch limbs
/// that are already non-zero ("tracked") and never enable a limb the creature doesn't use.
/// </summary>
public static class PetHealth
{
    /// <summary>Limb keys the pet actually uses (current value &gt; 0).</summary>
    public static IReadOnlyList<string> TrackedLimbs(WorldPet pet)
        => pet.LimbHealth.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();

    /// <summary>The strongest current limb value - the best proxy we have for "full".</summary>
    public static double MaxLimb(WorldPet pet)
        => pet.LimbHealth.Count == 0 ? 0 : pet.LimbHealth.Values.Max();

    /// <summary>Derived status (see <see cref="PetStatus"/>).</summary>
    public static PetStatus Status(WorldPet pet)
    {
        if (pet.IsDead) return PetStatus.Dead;
        if (pet.TotalHealth <= 0) return PetStatus.Downed;
        var max = MaxLimb(pet);
        var anyDamaged = pet.LimbHealth.Any(kv => kv.Value > 0 && kv.Value < max);
        return anyDamaged ? PetStatus.Hurt : PetStatus.Healthy;
    }

    /// <summary>A new limb map with every tracked limb raised to the pet's strongest limb.</summary>
    public static Dictionary<string, double> HealedLimbs(WorldPet pet)
        => SetTracked(pet, MaxLimb(pet));

    /// <summary>A new limb map with every limb set to 0 (downs the pet).</summary>
    public static Dictionary<string, double> DownedLimbs(WorldPet pet)
        => pet.LimbHealth.ToDictionary(kv => kv.Key, _ => 0.0, StringComparer.Ordinal);

    /// <summary>
    /// A new limb map for reviving: tracked limbs restored to <paramref name="fraction"/>
    /// of full (default ~25%, per the wiki's companion-revive behaviour, minimum 1).
    /// </summary>
    public static Dictionary<string, double> RevivedLimbs(WorldPet pet, double fraction = 0.25)
    {
        var max = MaxLimb(pet);
        return SetTracked(pet, max > 0 ? Math.Max(1, max * fraction) : 1);
    }

    /// <summary>A copy of the limb map with every tracked (non-zero) limb set to <paramref name="target"/>.</summary>
    private static Dictionary<string, double> SetTracked(WorldPet pet, double target)
    {
        var result = new Dictionary<string, double>(pet.LimbHealth, StringComparer.Ordinal);
        foreach (var key in TrackedLimbs(pet))
        {
            result[key] = target;
        }
        return result;
    }
}
