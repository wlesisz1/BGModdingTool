using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class ModRecipeCatalogTests
{
    [Theory]
    [InlineData("EET/SETUP-EET.TP2", "eet")]
    [InlineData("setup-eet.tp2", "eet")]
    [InlineData("EET/EET_END.TP2", "eet_end")]
    [InlineData("SETUP-EET_END.TP2", "eet_end")]
    [InlineData("DLCMERGER/SETUP-DLCMERGER.TP2", "dlcmerger")]
    [InlineData("IWD_EET/SETUP-IWD_EET.TP2", "iwd_eet")]
    [InlineData("IWD1_EET/IWD1_EET.TP2", "iwd1_eet")]
    [InlineData("IWD2_EET/IWD2_EET.TP2", "iwd2_eet")]
    [InlineData("NWNFORBG/SETUP-NWNFORBG.TP2", "nwnforbg")]
    [InlineData("BP-BGT-WORLDMAP/SETUP-BP-BGT-WORLDMAP.TP2", "bp-bgt-worldmap")]
    [InlineData("HOW_EET/HOW_EET.TP2", "how_eet")]
    [InlineData("IWD_EET-PARTY-BANTER/SETUP-IWD_EET-PARTY-BANTER.TP2", "iwd_eet-party-banter")]
    public void Find_matches_known_tp2_spellings(string tp2, string expectedKey)
    {
        var recipe = ModRecipeCatalog.Find(tp2);
        Assert.NotNull(recipe);
        Assert.Equal(expectedKey, recipe.Key);
    }

    [Fact]
    public void Find_returns_null_for_unknown_mods()
    {
        Assert.Null(ModRecipeCatalog.Find("STRATAGEMS/SETUP-STRATAGEMS.TP2"));
    }

    [Fact]
    public void Eet_end_must_be_last_and_has_no_args()
    {
        var recipe = ModRecipeCatalog.Find("SETUP-EET_END.TP2")!;
        Assert.True(recipe.MustBeLast);
        Assert.Null(recipe.ArgsTemplate);
    }

    [Fact]
    public void Self_contained_gate_project_mods_need_no_path()
    {
        Assert.Null(ModRecipeCatalog.Find("IWD1_EET/IWD1_EET.TP2")!.ArgsTemplate);
        Assert.Null(ModRecipeCatalog.Find("IWD2_EET/IWD2_EET.TP2")!.ArgsTemplate);
        Assert.Null(ModRecipeCatalog.Find("NWNFORBG/SETUP-NWNFORBG.TP2")!.ArgsTemplate);
    }

    [Fact]
    public void How_eet_recommends_stdin_answer()
    {
        Assert.Equal("1", ModRecipeCatalog.Find("HOW_EET/HOW_EET.TP2")!.RecommendedStdin);
    }

    [Fact]
    public void RenderArgs_substitutes_quoted_path()
    {
        var recipe = ModRecipeCatalog.Find("SETUP-EET.TP2")!;
        var args = ModRecipeCatalog.RenderArgs(recipe, @"F:\gry\bgee\game");
        Assert.Equal("--args-list sp \"F:\\gry\\bgee\\game\"", args);
    }

    [Fact]
    public void RenderArgs_without_path_leaves_blocking_marker()
    {
        var recipe = ModRecipeCatalog.Find("SETUP-EET.TP2")!;
        var args = ModRecipeCatalog.RenderArgs(recipe, null);
        Assert.Contains(ModRecipe.UnresolvedMarker, args);
    }
}
