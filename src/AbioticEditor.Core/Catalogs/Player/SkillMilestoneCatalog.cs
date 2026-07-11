namespace AbioticEditor.Core.PlayerSaves;

/// <summary>One skill milestone: reaching <see cref="Level"/> grants <see cref="Perk"/>.</summary>
public sealed record SkillMilestone(int Level, string Perk, string Effect);

/// <summary>
/// Per-skill milestone perks and per-level passives, transcribed from
/// abioticfactor.wiki.gg/wiki/Skills (see docs/research-wiki-round10.md). Milestone
/// levels are irregular per skill (Fishing tops out at 15; Cooking has 8 milestones).
/// Display-only: the game re-derives perks from the skill level, so a save editor never
/// writes them.
/// </summary>
public static class SkillMilestoneCatalog
{
    /// <summary>Milestones for a skill, keyed by its DT_Skills display name. Empty if unknown.</summary>
    public static IReadOnlyList<SkillMilestone> For(string displayName)
        => Milestones.TryGetValue(Normalize(displayName), out var list) ? list : [];

    /// <summary>The skill's per-level passive bonus text, or null if unknown.</summary>
    public static string? PassiveFor(string displayName)
        => Passives.TryGetValue(Normalize(displayName), out var text) ? text : null;

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static readonly Dictionary<string, string> Passives = new()
    {
        ["sprinting"] = "Per level: +2 max stamina, +1% sprint speed, +2% stamina regeneration",
        ["strength"] = "Per level: +2 carrying capacity",
        ["throwing"] = "Per level: +10 projectile velocity, +2 damage",
        ["sneaking"] = "Per level: −5% enemy detection speed (caps at 95%)",
        ["blunt melee"] = "Per level: +1 blunt damage",
        ["sharp melee"] = "Per level: +1 sharp damage",
        ["accuracy"] = "Per level: −0.075 aim sway, −0.05 bullet spread",
        ["reloading"] = "Per level: faster reload speed",
        ["fortitude"] = "Per level: +2 max health",
        ["crafting"] = "Per level: faster crafting, −2% crafted-item durability loss chance",
        ["construction"] = "Per level: faster building and deconstructing",
        ["first aid"] = "Per level: medical items heal more",
        ["cooking"] = "Every 5 levels: fried and baked food quality upgrades",
        ["agriculture"] = "Per level: harvested plants regrow faster",
        ["fishing"] = "Per level: stronger line (resists breaking during catches)",
    };

    private static readonly Dictionary<string, IReadOnlyList<SkillMilestone>> Milestones = new()
    {
        ["sprinting"] =
        [
            new(5, "Athletic", "Chance to not lose stamina for any action"),
            new(10, "Lightspeed", "Sprint 5% faster overall (on top of passive gains)"),
            new(15, "Red Shift", "While sprinting, enemies are more likely to miss when targeting you"),
            new(20, "Out Of My Way!", "Sprinting speed increases over several seconds"),
        ],
        ["strength"] =
        [
            new(5, "Step Aside", "Shake Vending Machines & stomp Carbuncles in 1 hit"),
            new(8, "Heavy Weapons", "Strong enough to properly wield heavy melee weapons"),
            new(12, "Nerd Rage", "When bleeding near a hostile enemy, melee damage and speed are enhanced"),
            new(15, "Heavy Armor Specialization", "Can wear any weight of armor"),
            new(20, "Superior Gains", "All items weigh 25% less"),
        ],
        ["throwing"] =
        [
            new(2, "Underhand Toss", "Gentle underhand toss with all throwable items"),
            new(5, "Projectile Pickup", "Automatically pick up projectiles you've thrown"),
            new(8, "Hazy Recollection", "Weapons thrown by you are highlighted for 30 seconds"),
            new(10, "Projectile Predictor", "See the predicted path of your throwables"),
            new(12, "Tinkerer", "Crafting throwables yields 1 additional item"),
            new(15, "Terminal Velocity", "Hitting enemies with a thrown weapon staggers them"),
            new(20, "Quantum Displacement", "Thrown items sometimes split into 2 additional projectiles"),
        ],
        ["sneaking"] =
        [
            new(2, "Sneak Attack", "First attack on an idle enemy has 25% chance to deal double damage"),
            new(5, "Biotic Shadow", "Move 15% faster while crouched; can perform a roll"),
            new(8, "Nimble", "Don't trigger Tripwires, Tripwire Lasers, or Carbuncles"),
            new(10, "Night Worker", "Much quieter footsteps; increased Sneak Attack chance at night"),
            new(15, "Office Assassin", "Attacks on unaware enemies always deal double damage"),
            new(20, "Interdimensional", "Enemy attacks just sometimes... miss?"),
        ],
        ["blunt melee"] =
        [
            new(3, "Power Attack", "Heavy windup attacks with blunt melee weapons"),
            new(7, "Battle Charge", "Attacking during a sprint becomes a driving power attack"),
            new(10, "Stunning Slam", "Stagger enemies more easily with blunt melee"),
            new(12, "Power Hungry", "Power Attacks use 50% less stamina per swing"),
            new(15, "Crusher", "Enemies resistant to Blunt... aren't"),
            new(20, "Smash", "Chance to instantly explode human-sized or smaller enemies"),
        ],
        ["sharp melee"] =
        [
            new(3, "Sharp Throwing", "Can throw sharp melee weapons"),
            new(5, "Clean Cutter", "25% higher chance to not end up with Bio Scrap when butchering"),
            new(9, "No Survivors", "Enemies killed by sharp melee attacks outright die (no second forms)"),
            new(12, "Slice n' Dice", "Higher chance to cause Severe Bleeding"),
            new(15, "Eviscerator", "Cut through Sharp-resistant enemies as if not resistant"),
            new(20, "Heartseeker", "Very small chance to instantly kill small/medium enemies"),
        ],
        ["accuracy"] =
        [
            new(5, "Squint", "Alt-fire while aiming a ranged weapon zooms vision"),
            new(7, "Mil-Spec", "Confidence to handle advanced firearms (Bio-Metric Armwraps recipe)"),
            new(10, "Straight as an Arrow", "Projectiles less likely to break on impact"),
            new(13, "Boomstick", "Smaller creatures may flee when you fire"),
            new(15, "Bio-Mimic Armwraps", "Advanced Bio-Mimic Armwraps recipe"),
            new(18, "Bio-Fusion Imitator", "Advanced Bio-Fusion Imitator trinket recipe"),
            new(20, "Stopping Power", "Projectiles/bullets have a small chance to stun the target"),
        ],
        ["reloading"] =
        [
            new(3, "Ammo Crafter", "Craft ammo twice as fast"),
            new(5, "Just In Case", "Reload weapons you aren't otherwise qualified to use"),
            new(10, "Basic Geometry", "Less clumsy at reloading all weaponry"),
            new(15, "Speedloader", "Sprint and reload at the same time"),
            new(20, "Loose Rounds", "Sometimes find spare rounds when reloading"),
        ],
        ["fortitude"] =
        [
            new(5, "Habituation", "Regenerate health a bit more frequently"),
            new(8, "Group Effort", "Resting within 8 m of other resting scientists doubles rest rate"),
            new(10, "Spongy Tissue", "Slightly reduced fall damage and vehicle impact damage"),
            new(15, "Reflective Mantle", "Melee contact may reflect damage back to the enemy"),
            new(20, "Strong Ecosystem", "Regenerate 1 health every second"),
        ],
        ["crafting"] =
        [
            new(2, "More Bench", "First set of Crafting Bench upgrades"),
            new(5, "Mega Bench", "Second tier of Crafting Bench upgrades"),
            new(8, "Beautiful Blueprints", "Recipes shared with you skip the research phase"),
            new(10, "Eye For Detail", "Crafted items gain temporary bonus durability"),
            new(15, "Super Bench", "Final tier of Crafting Bench upgrades"),
            new(20, "Precision Engineering", "Small chance to consume 1 less item in multi-item recipes"),
        ],
        ["construction"] =
        [
            new(5, "Pack Your Desk", "Package small deployables twice as fast"),
            new(10, "Razed With Care", "50% chance of double resources when dismantling"),
            new(15, "Castle Doctrine", "Fortification build costs reduced by half"),
            new(20, "Spontaneous Furniture Event", "Chance of double furniture when packaging non-player-built items"),
        ],
        ["first aid"] =
        [
            new(3, "Refreshing Touch", "Healing teammates also grants them +5 hydration"),
            new(4, "Rad Remover", "Recipe: Pentetic Acid Syringe (radiation removal)"),
            new(5, "Bedside Manner", "See others' medical debuffs when close"),
            new(8, "Bonesetter", "Applied splints heal bones faster"),
            new(11, "Aftercare", "Revived teammates get a healing buff; +20% boost when using a medical item"),
            new(15, "Combat Doctor", "Apply medical items to self and others twice as fast"),
            new(20, "Brink of Death", "Your revives grant an Extra Down instead of outright death"),
        ],
        ["cooking"] =
        [
            new(3, "Soupsmith", "Cook soups in a pot with water and ingredients"),
            new(5, "Mother Knows Best", "See nearby players' hunger and thirst"),
            new(8, "Prep Chef", "All cooking recipes craft in half the time"),
            new(10, "Hearty & Oven Recipes", "Extra health during food regen; Convection Oven + Raw Dough recipes"),
            new(12, "Expert Gibbing", "Advanced knife recipe; corpse cutting without useless scrap"),
            new(15, "Chef Sense", "Notified when food finishes cooking, even remotely"),
            new(17, "Serving Seconds", "Soups, pies, etc. contain 2 extra portions"),
            new(20, "Fast Food", "All food cooks 25% faster on cooktops/ovens"),
        ],
        ["agriculture"] =
        [
            new(4, "Fertilizer Tier 1", "Recipe: Anomalous Compost (T1)"),
            new(8, "Fertilizer Tier 2", "Recipe: Anomalous Fertilizer (T2)"),
            new(10, "Photosynthetic Synergy", "Garden plants within 15 m grow 10% faster"),
            new(13, "Entangled Ecosystem", "Nearby plants collectively consume less water"),
            new(15, "Fertilizer Tier 3", "Recipe: Anomalous Plant Food (T3)"),
            new(20, "Prudent Plucking", "+1 extra harvest when gathering wild plants"),
        ],
        ["fishing"] =
        [
            new(3, "Fish Sense", "Detect fishing hotspots; craft Fish Trap"),
            new(5, "Tacklebox", "Recipe: Tacklebox"),
            new(10, "Lucky Fishing Hat", "Recipes: Lucky Fishing Hat and Mud Waders"),
            new(12, "Bait and Switch", "33% chance to keep bait when fishing up junk"),
            new(15, "Freshwater Friends", "Certain underwater creatures no longer feel threatened"),
        ],
    };
}
