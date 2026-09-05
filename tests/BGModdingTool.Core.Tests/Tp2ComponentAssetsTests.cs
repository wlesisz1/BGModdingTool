using BGModdingTool.Core.Services;

namespace BGModdingTool.Core.Tests;

public class Tp2ComponentAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgmt-tp2img-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Maps_copied_images_to_components_by_order_and_designated()
    {
        var mod = Path.Combine(_root, "Helga");
        Directory.CreateDirectory(Path.Combine(mod, "Portraits"));
        foreach (var f in new[] { "HelgaL.bmp", "HelgaM.bmp", "AHelgaL.bmp" })
            File.WriteAllText(Path.Combine(mod, "Portraits", f), "x");
        File.WriteAllText(Path.Combine(mod, "setup-Helga.tp2"), """
            BACKUP ~Helga/backup~
            BEGIN @1
              COPY ~Helga/dummy.cre~ ~override~
            BEGIN @64
            SUBCOMPONENT @61
              COPY ~%MOD_FOLDER%/Portraits/HelgaL.bmp~ ~override/HelgaL.bmp~
              COPY ~%MOD_FOLDER%/Portraits/HelgaM.bmp~ ~override/HelgaM.bmp~
            BEGIN @65
            SUBCOMPONENT @61
              COPY ~%MOD_FOLDER%/Portraits/AHelgaL.bmp~ ~override/HelgaL.bmp~
            BEGIN @70 DESIGNATED 10
              COPY ~Helga/Portraits/missing.bmp~ ~override~
            """);

        var map = Tp2ComponentAssets.ImagesByComponent(_root, "Helga/setup-Helga.tp2");

        Assert.False(map.ContainsKey(0));                         // no images in the main component
        Assert.Equal(2, map[1].Count);                            // default portrait: L + M
        Assert.EndsWith("HelgaL.bmp", map[1][0]);
        Assert.Single(map[2]);                                    // alt portrait
        Assert.EndsWith("AHelgaL.bmp", map[2][0]);
        Assert.False(map.ContainsKey(10));                        // missing file is skipped
    }
}
