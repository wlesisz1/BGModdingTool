using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class WeiduLogParserTests
{
    private const string SampleLog = """
        // Log of Currently Installed WeiDU Mods
        // The top of the file is the 'oldest' mod
        // ~TP2_File~ #language_number #component_number // [Subcomponent Name -> ] Component Name [ : Version]
        ~BG2FIXPACK/SETUP-BG2FIXPACK.TP2~ #0 #0 // BG2 Fixpack - Core Fixes: v13
        ~STRATAGEMS/SETUP-STRATAGEMS.TP2~ #0 #1000 // Initialise AI components (required for tactical and AI components): 35.19
        ~STRATAGEMS/SETUP-STRATAGEMS.TP2~ #0 #5900 // Standardise spells: BG1 vs BG2 -> Introduce BG2 spell scrolls into BG1: 35.19
        ~CDTWEAKS/SETUP-CDTWEAKS.TP2~ #2 #60 // Weapon Animation Tweaks: v16
        """;

    [Fact]
    public void Parses_entries_with_language_component_name_and_version()
    {
        var entries = WeiduLogParser.Parse(SampleLog);

        Assert.Equal(4, entries.Count);
        Assert.Equal("BG2FIXPACK/SETUP-BG2FIXPACK.TP2", entries[0].Tp2);
        Assert.Equal(0, entries[0].ComponentNumber);
        Assert.Equal("v13", entries[0].Version);
        Assert.Equal("BG2 Fixpack - Core Fixes", entries[0].ComponentName);

        Assert.Equal(2, entries[3].LanguageIndex);
        Assert.Equal(60, entries[3].ComponentNumber);
    }

    [Fact]
    public void Skips_comment_and_blank_lines()
    {
        var entries = WeiduLogParser.Parse("// only comments\n\n// more\n");
        Assert.Empty(entries);
    }

    [Fact]
    public void Groups_consecutive_same_mod_entries_into_build_entries()
    {
        var build = WeiduLogParser.ToBuildEntries(WeiduLogParser.Parse(SampleLog));

        Assert.Equal(3, build.Count);
        Assert.Equal("STRATAGEMS/SETUP-STRATAGEMS.TP2", build[1].Tp2);
        Assert.Equal([1000, 5900], build[1].Components);
        Assert.Equal(2, build[2].LanguageIndex);
    }
}
