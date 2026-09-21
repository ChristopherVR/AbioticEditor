namespace AbioticEditor.Core.Codex;

/// <summary>
/// Maps a creature's display name (round 99 moved the old <c>LiveNpcsTab.razor</c> into the
/// merged <c>WorldNpcsTab.razor</c>'s Creatures chip - see that component's own
/// <c>HeuristicName</c>, which strips the <c>NPC_Monster_</c>/<c>NPC_</c> class prefix and
/// title-cases what's left; unchanged logic, just relocated) to its image file name on
/// abioticfactor.wiki.gg.
///
/// Researched 2026-09-17 against the roster on <c>/wiki/Creatures</c> (Combat Creatures, Combat
/// Humanoids, Combat Robots, Non-Combat Creatures - roughly 85 entries at the time), expanded the
/// same day by verifying each remaining page-title guess against <c>Special:FilePath</c> (does it
/// actually redirect to an image, not the wiki's own rate-limit page - both can answer HTTP 429
/// under load, so a guess was only curated on a confirmed image redirect). Unlike
/// <see cref="FishWikiImages"/> (which reuses irregular item-icon file names), most creature pages
/// simply use the page's own title as the image file name (spaces to underscores, a lowercase
/// <c>.png</c> extension, any parenthetical disambiguator like "(Enemy)" dropped) - the entries
/// below with no comment of their own are exactly that pattern, verified rather than assumed. The
/// remaining genuine exceptions: a handful of "Containment"-flavored creatures use an internal
/// IS-#### codename as their image instead of their page title (Darkwater Beast/IS-0023, The
/// Wayseeker/IS-0117, IS-0139/Crystalisk - this pattern likely repeats for creatures not curated
/// here yet), and two pages upload their art with an uppercase <c>.PNG</c> extension (Peccary,
/// Symphonist) rather than lowercase.
///
/// Round 91 walked the whole shipped roster instead: every NPC class the game's own
/// <c>DT_NPCList</c> table spawns (about 120 classes) keyed by the exact display name the live
/// CREATURES tab derives from the class, with each file read out of the wiki page's infobox
/// through the wiki API. About 100 of those classes now resolve to a picture; the rest have no
/// wiki page or an image-less one and are listed in the block's own comment below. ("Exor
/// Pikeman", searched for by that name in an earlier round and not found, turned out to be what
/// the game and the wiki both call "Exor Cha".)
///
/// <see cref="CandidatesFor"/> tries the curated file first, then the page-title guess, then - for
/// a multi-word name - the words in reverse order, since a live class name's own word order (e.g.
/// <c>NPC_Monster_Pest_Electro_C</c> -> "Pest Electro") does not always match the wiki's page
/// title order (e.g. "Electro-Pest") the way <c>PetCatalog.CompendiumTextureRefs</c> already had
/// to account for with the same family/variant class-naming shape. A wrong guess simply resolves
/// to no image (see <c>WikiImageCache</c>), never an error.
/// </summary>
public static class CreatureWikiImages
{
    /// <summary>Display name (case-insensitive) -> verified wiki file name.</summary>
    private static readonly Dictionary<string, string> ByDisplayName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // "Containment"-flavored creatures whose wiki art uses an internal codename instead
            // of the page title - keyed by both the page title and the codename itself, since it
            // is not evidenced live which one a connected game's class name actually derives to.
            ["IS-0139"] = "Crystalisk.png",
            ["Crystalisk"] = "Crystalisk.png",
            ["Darkwater Beast"] = "IS-0023.png",
            ["IS-0023"] = "IS-0023.png",
            ["The Wayseeker"] = "IS-0117.png",
            ["IS-0117"] = "IS-0117.png",
            ["Security Bot"] = "T1.png",
            // The live class is named base-then-variant (NPC_Monster_Pest_Electro, matching
            // PetCatalog's own curated friendly name "Electro Pest" for the very same class -
            // see PetCatalog.cs), so a derived display name could read either "Pest Electro" or
            // "Electro Pest" depending on which convention the caller applies; the wiki page
            // itself is "Electro-Pest" (hyphenated, not underscored) either way, which neither
            // guess in CandidatesFor would reach on its own.
            ["Pest Electro"] = "Electro-Pest.png",
            ["Electro Pest"] = "Electro-Pest.png",
            // Page-title matches, but uploaded with an uppercase extension - curated rather than
            // relying on CandidatesFor's lowercase guess plus a case-insensitive wiki lookup that
            // is not guaranteed.
            ["Peccary"] = "Peccary.PNG",
            ["Symphonist"] = "Symphonist.PNG",
            // Verified 2026-09-17 by resolving each page-title guess against
            // Special:FilePath and confirming it actually redirects to an image (a real 404,
            // not the wiki's rate limiter, which also answers HTTP 429 on a genuine miss under
            // load - see docs/reference/research/ for how that was told apart here). These all
            // matched the plain title-with-underscores pattern exactly, no codename or casing
            // exception needed, so curating them mainly buys the offline bundle (AllWikiFiles),
            // not a different lookup path.
            ["Alpha Peccary"] = "Alpha_Peccary.png",
            ["Armored Exor"] = "Armored_Exor.png",
            ["Behemoth"] = "Behemoth.png",
            ["Big Hive Larva"] = "Big_Hive_Larva.png",
            ["Big Larva"] = "Big_Larva.png",
            ["Bigfoot"] = "Bigfoot.png",
            ["Bigogi"] = "Bigogi.png",
            ["Bogman"] = "Bogman.png",
            ["Bombogi"] = "Bombogi.png",
            ["Carbuncle"] = "Carbuncle.png",
            ["Composer"] = "Composer.png",
            ["Exor"] = "Exor.png",
            ["Exor Cha"] = "Exor_Cha.png",
            ["Exor Monk"] = "Exor_Monk.png",
            ["Hive Larva"] = "Hive_Larva.png",
            ["Krasue"] = "Krasue.png",
            ["Larva"] = "Larva.png",
            ["Mushroom Carbuncle"] = "Mushroom_Carbuncle.png",
            // From this class's own original research pass (2026-09-17): read directly off each
            // page's infobox rather than guessed, but never added to the dictionary until now.
            // "Nyth the Gluttonous" (the wiki page title) is deliberately NOT included here: its
            // live display name would just be "Nyth", and whether the uploaded file is
            // "Nyth.png" or "Nyth_the_Gluttonous.png" was never actually confirmed, so guessing
            // here would risk a wrong curated entry (worse than none, since a curated miss is
            // tried before the runtime guess) rather than a missing one.
            ["Zombie"] = "Zombie.png",
            ["Archivist"] = "Archivist.png",
            ["Guard"] = "Guard.png",
            ["Corpsewalker"] = "Corpsewalker.png",
            ["Close-Quarters Combatant"] = "Close-Quarters_Combatant.png",
            ["Mother of the Fallow"] = "Mother_of_the_Fallow.png",
            ["Mystagogue Eye"] = "Mystagogue_Eye.png",
            ["Tainted Carbuncle"] = "Tainted_Carbuncle.png",
            ["Tarasque"] = "Tarasque.png",
            ["Furfur"] = "Furfur.png",
            ["The Moving Box"] = "The_Moving_Box.png",
            // The Order and Canaanite humanoid factions, verified the same way as the batch
            // above (Special:FilePath redirect check, plain title-with-underscores pattern).
            ["Garage Rat"] = "Garage_Rat.png",
            ["Lab Rat"] = "Lab_Rat.png",
            ["Medic"] = "Medic.png",
            ["Mountaineer"] = "Mountaineer.png",
            ["Sapper"] = "Sapper.png",
            ["Shock Trooper"] = "Shock_Trooper.png",
            ["Sniper"] = "Sniper.png",
            ["Trooper"] = "Trooper.png",
            ["Farmer"] = "Farmer.png",
            ["Lodeite"] = "Lodeite.png",
            ["Niketas"] = "Niketas.png",
            ["Torchbearer"] = "Torchbearer.png",
            ["Woodsman"] = "Woodsman.png",
            // Round 91, keyed by what the LIVE tab actually derives from a class name (a live
            // report: "Robot Defense" showed no picture - the class is NPC_Robot_Defense_C, the
            // game calls it "Defense Robot" and the wiki "Defense Bot", so no guess could reach
            // it, and "Security Bot" above never matched the live "Robot Security" either).
            // Every shipped NPC class was walked (tests/AbioticEditor.Probes/NpcRosterProbe.cs
            // dumps DT_NPCList, the game's own class -> display-name table), its wiki page found
            // from that display name against the wiki's Creatures roster, and the file below
            // read straight out of each page's infobox through the wiki's own API (action=parse)
            // rather than guessed. The three Combat Robots' infoboxes use tier icons (T1/T2/T3),
            // the Order humanoids use render shots with irregular names, and a few pages spell
            // their file differently from their title (Tarecarry, Rattis Pestis, Winter Peccary).
            // Classes with no wiki page or an image-less page (Reaper Scout, Thespian, Clericus,
            // the Dark Lens "of the Fallow" variants, the Magma/Verdant Skinks, Zombie Scientist /
            // Dam Worker, Sir Ogi, Order Trooper's page, the training dummy and hologram, the
            // VOTV UFO/Wisp cameos) are deliberately left out rather than pointed at a wrong file.
            ["Robot Defense"] = "T3.png",
            ["Robot Containment"] = "T2.png",
            ["Robot Security"] = "T1.png",
            ["Robot Corrupt"] = "Corrupt_Robot.png",
            ["Reaper"] = "ReaperCalm.png",
            ["Assassin"] = "Crystalisk.png",
            ["Interfector"] = "Order_Interfector.png",
            // Narrative hologram conversation rows call this character "Order Interfector";
            // it is the same verified portrait as the combat-creature entry above.
            ["Order Interfector"] = "Order_Interfector.png",
            ["Bog Man"] = "Bogman.png",
            ["Boxy"] = "The_Moving_Box.png",
            ["Exor Ally"] = "Exor_Spirit.png",
            ["Exor Pikeman"] = "Exor_Cha.png",
            ["Gatekeeper Chieftain"] = "Chieftain.png",
            ["Gatekeeper Grunt"] = "Neophyte.png",
            ["Gatekeeper Heavy"] = "Jotun.png",
            ["Gatekeeper Heavy Banner Speech"] = "Jotun.png",
            ["Gatekeeper Witch"] = "Witch.png",
            ["Mage"] = "Mystagogue.png",
            ["Mage Eye"] = "Mystagogue_Eye.png",
            ["Mage Eye Ally"] = "Mystagogue_Eye.png",
            ["Groupe Shield"] = "Rook_Enemy.png",
            ["Groupe Shotgunner"] = "Close-Quarters_Combatant.png",
            ["NPC Groupe Mountaineer"] = "Mountaineer.png",
            ["Lab Rat Garage"] = "Garage_Rat.png",
            ["Mgt Larva"] = "Larva.png",
            ["Mgt Larva Big"] = "Big_Larva.png",
            ["Mgt Larva Big Hive"] = "Big_Hive_Larva.png",
            ["Mgt Larva Hive"] = "Hive_Larva.png",
            ["Order Grunt Shot Trooper"] = "Shock_Trooper.png",
            ["Peccary Armored"] = "Tarecarry.png",
            ["Pest Rat"] = "Rattis_Pestis.png",
            ["Pillager Farmer"] = "Farmer.png",
            ["Pillager Musketeer"] = "Guard.png",
            ["Pillager Musketeer Guard"] = "Guard.png",
            ["Pillager Preacher"] = "Lodeite.png",
            ["Pillager Preacher Niketas BOSS"] = "Niketas.png",
            ["Pillager Torchbearer"] = "Torchbearer.png",
            ["Pillager Woodsman"] = "Woodsman.png",
            ["Power Leech"] = "Power_Leech_Small.png",
            ["Power Leech Boss"] = "Power_Leech.png",
            ["Skink Basic"] = "Skink.png",
            ["Skink Crafted"] = "Skink.png",
            ["Soldier Captain"] = "Captain_render.png",
            ["Soldier Grunt"] = "Grunt_Order_render.png",
            ["Soldier Grunt Medic"] = "Medic.png",
            ["Soldier Grunt Shotgun"] = "Breacher.png",
            ["Soldier Grunt Sniper"] = "Sniper.png",
            ["VOTV Corpsewalker"] = "Corpsewalker.png",
            ["VOTV Furfur"] = "Furfur.png",
            ["Winter Sprite"] = "Lamogi.png",
            ["Winter Sprite BOSS"] = "Bigogi.png",
            ["Winter Sprite Bomb"] = "Bombogi.png",
            ["Snowman"] = "T_Compendium_Snowman.png",
            ["Leyak"] = "Leyak.PNG",
            ["Pest"] = "Pest.png",
            ["Yeti"] = "Yeti.png",
            ["Coworker"] = "Coworker.png",
            ["Flame Rat"] = "Flame_Rat.png",
            ["Peccary Alpha"] = "Alpha_Peccary.png",
            ["Peccary Sow"] = "Sow_Peccary.PNG",
            ["Peccary Snow"] = "Winter_Peccary.png",
            ["Peccary Mushroom"] = "Mushroom_Peccary.png",
            ["Peccary Electro"] = "Electro_Peccary.png",
            ["Peccary Volatile"] = "Volatile_Peccary.png",
            ["Pest Snow"] = "Winter_Pest.png",
            ["Pest Carbonated"] = "Carbonated_Pest.png",
            ["Pest Enlightened"] = "Enlightened_Pest.png",
            ["Pest Volatile"] = "Volatile_Pest.png",
            ["Exor Armored"] = "Armored_Exor.png",
            ["Exor Volatile"] = "Volatile_Exor.png",
            ["Carbuncle Tainted"] = "Tainted_Carbuncle.png",
            ["Carbuncle Mushroom"] = "Mushroom_Carbuncle.png",
        };

    /// <summary>The verified display-name -> wiki-file table (for coverage tests).</summary>
    public static IReadOnlyDictionary<string, string> KnownCreatures => ByDisplayName;

    /// <summary>Every verified wiki File name, de-duplicated - used to pre-download the offline
    /// fallback bundle; the speculative page-title/reversed-word guesses in
    /// <see cref="CandidatesFor"/> are excluded, the same way <see cref="FishWikiImages.AllWikiFiles"/>
    /// excludes its own guesses.</summary>
    public static IReadOnlyCollection<string> AllWikiFiles =>
        ByDisplayName.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Wiki image file names to try, in order, for a creature's display name: the curated file
    /// first, then the page-title guess (spaces to underscores, lowercase <c>.png</c>), then - for
    /// a multi-word name only - the same guess with the words reversed, covering a family/variant
    /// class name whose word order does not match the wiki's own page title. See this class's own
    /// remarks for why a curated entry is still needed for some creatures no guess can reach.
    /// </summary>
    public static IReadOnlyList<string> CandidatesFor(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Array.Empty<string>();
        var name = displayName.Trim();
        var candidates = new List<string>(3);

        if (ByDisplayName.TryGetValue(name, out var known)) candidates.Add(known);

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return candidates;

        var guess = $"{string.Join('_', words)}.png";
        if (!candidates.Contains(guess, StringComparer.OrdinalIgnoreCase)) candidates.Add(guess);

        if (words.Length > 1)
        {
            var reversedWords = words.Reverse().ToArray();
            var reversedGuess = $"{string.Join('_', reversedWords)}.png";
            if (!candidates.Contains(reversedGuess, StringComparer.OrdinalIgnoreCase)) candidates.Add(reversedGuess);
        }

        return candidates;
    }
}
