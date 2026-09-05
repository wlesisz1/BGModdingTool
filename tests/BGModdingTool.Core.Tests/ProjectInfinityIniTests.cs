using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class ProjectInfinityIniTests
{
    [Fact]
    public void Parses_metadata_section()
    {
        const string ini = """
            [Metadata]
            Name = Sword Coast Stratagems
            Author = DavidW
            Description = AI and tactics overhaul
            Type = BG:EE, BG2:EE, EET, IWD:EE
            After = ascension/ascension.tp2, bg2fixpack/setup-bg2fixpack.tp2
            Forum = https://www.gibberlings3.net/forums/

            [Other]
            Name = should be ignored
            """;

        var meta = ProjectInfinityIni.Parse(ini);

        Assert.Equal("Sword Coast Stratagems", meta.Name);
        Assert.Equal("DavidW", meta.Author);
        Assert.Equal(["BG:EE", "BG2:EE", "EET", "IWD:EE"], meta.Games);
        Assert.Equal(["ASCENSION/ASCENSION.TP2", "BG2FIXPACK/SETUP-BG2FIXPACK.TP2"], meta.After);
    }

    [Fact]
    public void Missing_metadata_section_yields_empty_metadata()
    {
        var meta = ProjectInfinityIni.Parse("[Config]\nFoo = bar\n");
        Assert.Null(meta.Name);
        Assert.Empty(meta.Games);
    }
}
