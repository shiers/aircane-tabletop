using Aircane.Infrastructure.DocumentProcessing;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>Tests for <see cref="StatBlockDetector"/>.</summary>
public class StatBlockDetectorTests
{
    // ── Header detection ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Giant Crocodile", "Huge beast, unaligned")]
    [InlineData("Veteran", "Medium humanoid (any race), any alignment")]
    [InlineData("Ancient Red Dragon", "Gargantuan DRAGON, chaotic evil")]   // type words are case-insensitive
    [InlineData("## Giant Crocodile", "*Huge beast, unaligned*")]            // markdown decoration is ignored
    [InlineData("  Giant Crocodile  ", "  Huge beast, unaligned  ")]         // whitespace is trimmed
    public void StatBlockDetector_DetectsNamePlusSizeTypeLine_AsStatBlock(string name, string next)
    {
        Assert.True(StatBlockDetector.IsStatBlockHeader(name, next));
    }

    [Fact]
    public void StatBlockDetector_DetectsNamePlusCreatureN_AsPf2eStatBlock()
    {
        Assert.True(StatBlockDetector.IsStatBlockHeader("Giant Crocodile", "Creature 6"));
        Assert.True(StatBlockDetector.IsStatBlockHeader("GIANT CROCODILE", "CREATURE 6"));
    }

    [Theory]
    [InlineData("Giant Crocodile — Huge beast, unaligned")]
    [InlineData("Giant Crocodile – Huge beast, unaligned")]
    [InlineData("Giant Crocodile - Huge beast, unaligned")]
    public void StatBlockDetector_DetectsInlineSizeType_AsStatBlock(string line)
    {
        Assert.True(StatBlockDetector.IsStatBlockHeader(line, null));
    }

    [Theory]
    [InlineData("The party walked through the forest.", "They saw a large beast on the road.")]
    [InlineData("Chapter 3", "The Dark Forest")]
    [InlineData("Treasure", "A chest of 500 gold pieces")]
    [InlineData("", null)]
    public void StatBlockDetector_IgnoresNormalProse_NotStatBlock(string line, string? next)
    {
        Assert.False(StatBlockDetector.IsStatBlockHeader(line, next));
    }

    [Theory]
    [InlineData("Large coffee table", "Medium rare steak")]            // size word alone is not enough
    [InlineData("Dinner menu", "Large coffee, black")]                 // size + non-type word
    [InlineData("Huge", "beast")]                                      // split across lines is not a size/type line
    [InlineData("Small plates", "Huge portions, unaligned")]           // no type word
    public void StatBlockDetector_IgnoresPartialMatch_NotStatBlock(string line, string next)
    {
        Assert.False(StatBlockDetector.IsStatBlockHeader(line, next));
        Assert.All(StatBlockDetector.ExtractStatBlocks(line + "\n" + next), s => Assert.Equal(SegmentKind.Prose, s.Kind));
    }

    [Fact]
    public void StatBlockDetector_Prose_IsASingleProseSegment()
    {
        var text = "The party walked through the forest and met a large beast. It was Medium in height.";

        var segments = StatBlockDetector.ExtractStatBlocks(text);

        var only = Assert.Single(segments);
        Assert.Equal(SegmentKind.Prose, only.Kind);
        Assert.Equal(text, only.Text);
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private const string Bestiary =
        "Goblin\nSmall humanoid (goblinoid), neutral evil\nArmor Class 15\nHit Points 7 (2d6)\n" +
        "Actions\nScimitar. Melee Weapon Attack: +4 to hit. Hit: 5 (1d6 + 2) slashing damage.\n" +
        "Wolf\nMedium beast, unaligned\nArmor Class 13\nHit Points 11 (2d8 + 2)\n" +
        "Actions\nBite. Melee Weapon Attack: +4 to hit. Hit: 7 (2d4 + 2) piercing damage.\n" +
        "Giant Crocodile\nHuge beast, unaligned\nArmor Class 14\nHit Points 85\n" +
        "Actions\nBite. Melee Weapon Attack: +8 to hit. Hit: 21 piercing damage.";

    [Fact]
    public void StatBlockDetector_ExtractsMultipleStatBlocks_FromBestiaryText()
    {
        var segments = StatBlockDetector.ExtractStatBlocks(Bestiary);

        Assert.Equal(["Goblin", "Wolf", "Giant Crocodile"], segments.Select(s => s.CreatureName));
        Assert.All(segments, s => Assert.Equal(SegmentKind.StatBlock, s.Kind));
        // The "Actions" label inside each block does not end it.
        Assert.All(segments, s => Assert.Contains("Actions\n", s.Text));
        Assert.Contains("Scimitar", segments[0].Text);
        Assert.DoesNotContain("Wolf", segments[0].Text);
    }

    [Fact]
    public void StatBlockDetector_ProseBeforeFirstStatBlock_IsProseSegment()
    {
        var text = "The marsh is home to many dangers.\n\n" + StatBlockFixtures.GiantCrocodileLines;

        var segments = StatBlockDetector.ExtractStatBlocks(text);

        Assert.Equal([SegmentKind.Prose, SegmentKind.StatBlock], segments.Select(s => s.Kind));
        Assert.Equal("The marsh is home to many dangers.\n\n", segments[0].Text);
        Assert.StartsWith("Giant Crocodile\nHuge beast", segments[1].Text);
    }

    [Fact]
    public void StatBlockDetector_ProseBetweenStatBlocks_IsProseSegment()
    {
        var text =
            StatBlockFixtures.GiantCrocodileLines + "\n" +
            "## Chapter 2: The Swamp\n" +
            "The party wades deeper into the swamp, where the air grows thick.\n" +
            "Wolf\nMedium beast, unaligned\nArmor Class 13\nHit Points 11\nActions\nBite. Hit: 7 piercing damage.";

        var segments = StatBlockDetector.ExtractStatBlocks(text);

        Assert.Equal(
            [SegmentKind.StatBlock, SegmentKind.Prose, SegmentKind.StatBlock],
            segments.Select(s => s.Kind));
        Assert.StartsWith("## Chapter 2: The Swamp", segments[1].Text);
        Assert.DoesNotContain("Chapter 2", segments[0].Text);
        Assert.Equal("Wolf", segments[2].CreatureName);
    }

    [Fact]
    public void StatBlockDetector_IsolatedTitleCaseHeading_EndsTheStatBlock()
    {
        var text = StatBlockFixtures.GiantCrocodileLines + "\n\nThe Murky Swamp\n\nThe water is cold and still.";

        var segments = StatBlockDetector.ExtractStatBlocks(text);

        Assert.Equal([SegmentKind.StatBlock, SegmentKind.Prose], segments.Select(s => s.Kind));
        Assert.Contains("The Murky Swamp", segments[1].Text);
    }

    [Theory]
    [InlineData(StatBlockFixtures.GiantCrocodileLines)]
    [InlineData(StatBlockFixtures.GiantCrocodilePage)]
    [InlineData(Bestiary)]
    [InlineData("just some prose")]
    public void StatBlockDetector_Segments_CoverTheInputWithNoGapsOrOverlaps(string text)
    {
        var segments = StatBlockDetector.ExtractStatBlocks(text);

        Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
    }

    [Fact]
    public void StatBlockDetector_EmptyInput_ReturnsNoSegments()
    {
        Assert.Empty(StatBlockDetector.ExtractStatBlocks(""));
    }

    // ── Run-together text from real PDFs (no line breaks) ─────────────────────

    [Fact]
    public void StatBlockDetector_RunTogetherText_DetectsBothCreatures_AndNamesThem()
    {
        var segments = StatBlockDetector.ExtractStatBlocks(StatBlockFixtures.GiantCrocodilePage);

        Assert.Equal([SegmentKind.Prose, SegmentKind.StatBlock, SegmentKind.StatBlock], segments.Select(s => s.Kind));
        // OCR damage ("GIANT. CROCODILE") and ALL CAPS are tidied.
        Assert.Equal(["Giant Crocodile", "Giant Eagle"], segments.Skip(1).Select(s => s.CreatureName));
        Assert.Contains("Tail. Melee Weapon Attack", segments[1].Text);
        Assert.DoesNotContain("GIANT EAGLE", segments[1].Text);
        Assert.StartsWith("GIANT. CROCODILE Huge beast", segments[1].Text);
    }

    [Fact]
    public void StatBlockDetector_RunTogetherText_NameIsNotPulledInFromThePrecedingSentence()
    {
        var text = "Thugs are ruthless enforcers skilled at violence. VETERAN Medium humanoid (any race), " +
                   "any alignment Armor Class 17 (splint) Hit Points 58 (9d8 + 18) Speed 30ft. ACTIONS Multiattack. " +
                   "The veteran makes two longsword attacks.";

        var segments = StatBlockDetector.ExtractStatBlocks(text);

        Assert.Equal("Veteran", segments.Single(s => s.Kind == SegmentKind.StatBlock).CreatureName);
        Assert.Equal("Thugs are ruthless enforcers skilled at violence. ", segments[0].Text);
    }

    [Fact]
    public void StatBlockDetector_RunTogetherText_SizeAndTypeWithoutArmorClass_IsNotAStatBlock()
    {
        // Prose that happens to read "Name Large beast": without Armor Class it is not confirmed.
        var text = "The hunters spoke of Old Hungry Large beast tracks leading north.";

        Assert.All(StatBlockDetector.ExtractStatBlocks(text), s => Assert.Equal(SegmentKind.Prose, s.Kind));
    }

    [Fact]
    public void StatBlockDetector_RunTogetherPf2e_DetectsCreatureN()
    {
        var text = "Some earlier prose here. GIANT CROCODILE CREATURE 6 Perception +14 Skills Athletics +17";

        var block = StatBlockDetector.ExtractStatBlocks(text).Single(s => s.Kind == SegmentKind.StatBlock);

        Assert.Equal("Giant Crocodile", block.CreatureName);
    }

    [Fact]
    public void StatBlockDetector_NameWithConnectorWord_IsKeptWhole()
    {
        var text = "Intro text here. Gnoll Fang of Yeenoghu Medium fiend (gnoll), chaotic evil Armor Class 14 Hit Points 65";

        var block = StatBlockDetector.ExtractStatBlocks(text).Single(s => s.Kind == SegmentKind.StatBlock);

        Assert.Equal("Gnoll Fang of Yeenoghu", block.CreatureName);
    }

    [Fact]
    public void StatBlockDetector_RunTogetherText_OcrPageNumberIsNotPartOfTheName()
    {
        var text = "...a long description of the creature. I44 Djinni Large elemental, chaotic neutral Armor Class 17 Hit Points 161";

        var block = StatBlockDetector.ExtractStatBlocks(text).Single(s => s.Kind == SegmentKind.StatBlock);

        Assert.Equal("Djinni", block.CreatureName);
    }

    [Fact]
    public void StatBlockDetector_RunTogetherText_NameLostToOcr_IsAnUnnamedStatBlock()
    {
        var text = "Some earlier text here. I2 Medium monstrosity, unaligned Armor Class 12 Hit Points 22 (5d8)";

        var block = StatBlockDetector.ExtractStatBlocks(text).Single(s => s.Kind == SegmentKind.StatBlock);

        Assert.Null(block.CreatureName);
    }

    // ── Long blocks ───────────────────────────────────────────────────────────

    [Fact]
    public void StatBlockDetector_SplitAtActionBoundaries_ShortBlockIsReturnedWhole()
    {
        var parts = StatBlockDetector.SplitAtActionBoundaries(StatBlockFixtures.GiantCrocodileLines);

        Assert.Equal([StatBlockFixtures.GiantCrocodileLines], parts);
    }

    [Fact]
    public void StatBlockDetector_SplitAtActionBoundaries_CutsAtTheLastLabelThatFits()
    {
        var block = StatBlockFixtures.LongBlock("Ancient Red Dragon", 500, 2000, 1000, 1500);
        Assert.True(block.Length > StatBlockDetector.MaxStatBlockChars);

        var parts = StatBlockDetector.SplitAtActionBoundaries(block);

        Assert.True(parts.Count >= 2);
        Assert.All(parts, p => Assert.True(p.Length <= StatBlockDetector.MaxStatBlockChars));
        Assert.StartsWith("Ancient Red Dragon", parts[0]);
        Assert.StartsWith("Legendary Actions", parts[1]);
        Assert.Equal(
            block.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            parts.SelectMany(p => p.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Count());
    }

    [Fact]
    public void StatBlockDetector_SplitAtActionBoundaries_NoLabels_FallsBackToSentenceEnd()
    {
        var block = string.Concat(Enumerable.Repeat("The creature strikes the nearest foe with a mighty blow. ", 120));

        var parts = StatBlockDetector.SplitAtActionBoundaries(block);

        Assert.True(parts.Count >= 2);
        Assert.All(parts, p => Assert.True(p.Length <= StatBlockDetector.MaxStatBlockChars));
        Assert.All(parts, p => Assert.EndsWith(".", p));
    }
}
