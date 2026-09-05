using BGModdingTool.Core.Models;
using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class ReadmeFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-readme-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private int _packageCounter;

    // Each package gets its own sub-root so cases in one test don't see each other's files.
    private ModPackage Package(params string[] files)
    {
        var root = Path.Combine(_root, "pkg" + _packageCounter++);
        foreach (var f in files)
        {
            var p = Path.Combine(root, f);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "x");
        }
        Directory.CreateDirectory(root);
        return new ModPackage { Id = "p", RootPath = root, Tp2RelativePath = "mod/setup-mod.tp2", ModFolderName = "mod" };
    }

    [Fact]
    public void Prefers_english_readme_over_other_languages_and_html_over_txt()
    {
        var pkg = Package("mod/readme-mod.txt", "mod/lang/de_DE/readme-mod.html", "mod/lang/en_US/readme-mod.html");
        Assert.EndsWith(Path.Combine("en_US", "readme-mod.html"), ModLibrary.FindReadme(pkg));
    }

    [Fact]
    public void Falls_back_to_top_level_README_md_and_returns_null_when_absent()
    {
        Assert.EndsWith("README.md", ModLibrary.FindReadme(Package("README.md", "mod/x.tra")));
        Assert.Null(ModLibrary.FindReadme(Package("mod/only.tra")));
    }
}
