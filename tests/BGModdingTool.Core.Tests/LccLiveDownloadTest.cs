using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class LccLiveDownloadTest
{
    [Fact(Skip = "network diagnostic — run manually")]
    public async Task Live_download_works()
    {
        var root = Path.Combine(Path.GetTempPath(), "bgmt-lcclive-" + Guid.NewGuid().ToString("N")[..8]);
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        try
        {
            var db = new LccDatabase(paths);
            var count = await db.DownloadAsync();
            Assert.True(count > 1000);
            Assert.NotNull(db.FindByTp2("stratagems"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
