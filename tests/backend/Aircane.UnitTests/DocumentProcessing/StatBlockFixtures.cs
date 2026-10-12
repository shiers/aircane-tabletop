namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>Text fixtures shared by the stat block detector and chunker tests.</summary>
internal static class StatBlockFixtures
{
    /// <summary>
    /// A Monster Manual page as PdfPig extracted it from the real PDF (Appendix A, p.325): no line breaks,
    /// OCR damage in the heading ("GIANT. CROCODILE") and the ability scores interleaved. The Giant
    /// Crocodile block is ~900 characters but starts ~700 characters into the page, so a 1100-character window cuts it. Before the fix
    /// the creature stats and Bite were in one chunk and the Tail in the next.
    /// </summary>
    public const string GiantCrocodilePage =
        "0ft., swim 30ft. STR 13 (+1) DEX 15 (+2) Skills Stealth +4 CON 11 (+0) INT 1 (-5) WIS 9 (-1) " +
        "Senses blindsight 30ft., passive Perception 9 Languages-Challenge 1/8 (25 XP) Amphibious. " +
        "The crab can breathe air and water. ACTIONS CHA 3 (-4) " +
        "Claw. Melee Weapon Attack: +3 to hit, reach 5 ft., one target. Hit: 4 (1d6 + 1) bludgeoning damage, " +
        "and the target is grappled (escape DC 11). The crab has two claws, each of which can grapple only one " +
        "target. APPENDIX A: MISCELLANEOUS CREATURES , /{ 1 GIANT. CROCODILE Huge beast, unaligned " +
        "Armor Class 14 (natural armor) Hit Points 85 (9d12 + 27) Speed 30ft., swim 50 ft. STR 21 (+S) DEX 9 (-1) " +
        "Skills Stealth +5 CON 17 (+3) Senses passive Perception 10 Languages-Challenge 5 (1 ,800 XP) INT 2 (-4) " +
        "WIS 10 (+0) CHA 7 (-2) Hold Breath. The crocodile can hold its breath for 30 minutes. ACTIONS " +
        "Multiattack. The crocodile makes two attacks: one with its bite and one with its tail. " +
        "Bite. Melee Weapon Attack: +8 to hit, reach 5 ft., one target. Hit: 21 (3d10 + 5) piercing damage, and the " +
        "target is grappled (escape DC 16). Until this grapple ends, the target is restrained, and the crocodile " +
        "can't bite another target. " +
        "Tail. Melee Weapon Attack: +8 to hit, reach 10ft., one target not grappled by the crocodile. " +
        "Hit: 14 (2d8 + 5) bludgeoning damage. If the target is a creature, it must succeed on a DC 16 Strength " +
        "saving throw or be knocked prone. " +
        "GIANT EAGLE Large beast, neutral good Armor Class 13 Hit Points 26 (4d10 + 4) Speed 10ft., fly 80ft. " +
        "STR 16 (+3) DEX 17 (+3) Skills Perception +4 CON 13 (+1) Senses passive Perception 14 INT 8 (-1) " +
        "WIS 14 (+2) CHA 10 (+0) Languages Giant Eagle, understands Common and Auran but can't speak them " +
        "Challenge 1 (200 XP) Keen Sight. The eagle has advantage on Wisdom (Perception) checks that rely on sight. " +
        "ACTIONS Multiattack. The eagle makes two attacks: one with its beak and one with its talons.";

    /// <summary>The same creature in the line-based shape the spec describes.</summary>
    public const string GiantCrocodileLines =
        "Giant Crocodile\n" +
        "Huge beast, unaligned\n" +
        "Armor Class 14 (natural armor)\n" +
        "Hit Points 85 (9d12 + 27)\n" +
        "Speed 30 ft., swim 50 ft.\n" +
        "STR 21 (+5) DEX 9 (-1) CON 17 (+3) INT 2 (-4) WIS 10 (+0) CHA 7 (-2)\n" +
        "Challenge 5 (1,800 XP)\n" +
        "Hold Breath. The crocodile can hold its breath for 30 minutes.\n" +
        "Actions\n" +
        "Multiattack. The crocodile makes two attacks: one with its bite and one with its tail.\n" +
        "Bite. Melee Weapon Attack: +8 to hit, reach 5 ft., one target. Hit: 21 (3d10 + 5) piercing damage.\n" +
        "Tail. Melee Weapon Attack: +8 to hit, reach 10 ft., one target. Hit: 14 (2d8 + 5) bludgeoning damage.";

    /// <summary>Plain narrative prose with no stat blocks.</summary>
    public static string Prose(string label, int length)
    {
        var sb = new System.Text.StringBuilder();
        while (sb.Length < length)
            sb.Append($"The {label} travellers made camp beside the river and shared stories of the road. ");
        return sb.ToString(0, length - 1).TrimEnd() + ".";
    }

    /// <summary>
    /// A line-based stat block whose total length is roughly <paramref name="bodyChars"/> beyond the
    /// header, with Actions, Reactions and Legendary Actions sections of the given sizes.
    /// </summary>
    public static string LongBlock(string name, int traitsChars, int actionsChars, int reactionsChars, int legendaryChars) =>
        $"{name}\nHuge dragon, chaotic evil\nArmor Class 19 (natural armor)\nHit Points 256 (19d12 + 133)\n" +
        "Speed 40 ft., fly 80 ft.\nChallenge 17 (18,000 XP)\n" +
        Filler("Trait", traitsChars) +
        "Actions\n" + Filler("Attack", actionsChars) +
        "Reactions\n" + Filler("Reaction", reactionsChars) +
        "Legendary Actions\n" + Filler("Legendary", legendaryChars);

    private static string Filler(string label, int chars)
    {
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (sb.Length < chars)
            sb.Append($"{label} {i++}. Melee Weapon Attack: +8 to hit, reach 5 ft., one target. Hit: 21 piercing damage.\n");
        return sb.ToString();
    }
}
