using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class AssetLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-assets-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AssetLibrary _library;
    private readonly GameInstance _classicInstance;

    public AssetLibraryTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _library = new AssetLibrary(paths);
        _classicInstance = new GameInstance
        {
            Id = "inst", Name = "classic", GameType = GameType.BG2Classic,
            SourcePath = "n/a", RootPath = Path.Combine(_root, "instance"),
        };
        Directory.CreateDirectory(_classicInstance.GamePath);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Theory]
    [InlineData(new[] { "a/hero1L.bmp", "a/hero1M.bmp", "readme.txt" }, AssetKind.Portraits)]
    [InlineData(new[] { "s/female1a.wav", "s/female1b.wav" }, AssetKind.Soundset)]
    [InlineData(new[] { "music/theme.mus", "music/theme/thema.acm" }, AssetKind.Music)]
    [InlineData(new[] { "override/x.2da", "override/y.itm" }, AssetKind.Override)]
    public void DetectKind_guesses_from_file_types(string[] files, AssetKind expected)
    {
        Assert.Equal(expected, AssetLibrary.DetectKind(files));
    }

    [Fact]
    public void Ee_games_deploy_portraits_to_documents_and_classic_to_game_dir()
    {
        var pkg = new AssetPackage { Id = "p", Name = "p", RootPath = "n/a", Kind = AssetKind.Portraits };
        var ee = new GameInstance { Id = "e", Name = "ee", GameType = GameType.EET, SourcePath = "n/a", RootPath = _root };

        Assert.EndsWith(Path.Combine("Baldur's Gate II - Enhanced Edition", "portraits"), AssetLibrary.TargetDirectory(pkg, ee));
        Assert.True(AssetLibrary.TargetIsOutsideInstance(pkg, ee));
        Assert.Equal(Path.Combine(_classicInstance.GamePath, "portraits"), AssetLibrary.TargetDirectory(pkg, _classicInstance));
        Assert.False(AssetLibrary.TargetIsOutsideInstance(pkg, _classicInstance));
    }

    [Fact]
    public async Task Import_deploy_and_undeploy_roundtrip()
    {
        var src = Path.Combine(_root, "src-pack", "sub");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "heroL.bmp"), "img");
        File.WriteAllText(Path.Combine(src, "notes.txt"), "ignore me");

        var pkg = await _library.ImportDirectoryAsync(Path.Combine(_root, "src-pack"));
        Assert.Equal(AssetKind.Portraits, pkg.Kind);

        var deployment = await _library.DeployAsync(pkg, _classicInstance);
        var target = Path.Combine(_classicInstance.GamePath, "portraits", "heroL.bmp");
        Assert.Single(deployment.Files);          // notes.txt is not a portrait
        Assert.True(File.Exists(target));         // flattened: no "sub/" folder
        Assert.Single(_library.Deployments(pkg));

        var removed = _library.Undeploy(pkg, _classicInstance);
        Assert.Equal(1, removed);
        Assert.False(File.Exists(target));
        Assert.Empty(_library.Deployments(pkg));
    }
}
