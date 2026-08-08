using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathTemplatesTests
{
    private static ToolpathTemplateManager NewManager(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"vp-templates-{Guid.NewGuid():N}.json");
        return new ToolpathTemplateManager(path);
    }

    [Fact]
    public void Save_Load_Delete_RoundTrip()
    {
        var mgr = NewManager(out var path);
        try
        {
            var t = mgr.SaveTemplate("T-Shirt Profile", ToolpathTemplateType.Profile, "{\"cutMode\":\"outCut\"}");
            Assert.True(mgr.TemplateExists("t-shirt profile")); // case-insensitive
            Assert.Single(mgr.TemplatesFor(ToolpathTemplateType.Profile));
            Assert.Empty(mgr.TemplatesFor(ToolpathTemplateType.Pocket));

            // New manager over the same file reloads the template.
            var reloaded = new ToolpathTemplateManager(path);
            Assert.Single(reloaded.Templates);
            Assert.Equal("{\"cutMode\":\"outCut\"}", reloaded.ApplyTemplate(t.Id));

            reloaded.DeleteTemplate(t.Id);
            Assert.Empty(reloaded.Templates);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_File_Starts_Fresh()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-templates-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{not json");
            var mgr = new ToolpathTemplateManager(path);
            Assert.Empty(mgr.Templates);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class TilingEngineTests
{
    [Fact]
    public void Single_Tile_When_Sheet_Fits()
    {
        var tiles = TilingEngine.Tile(0, 0, 50, 50, 100, 100);
        Assert.Single(tiles);
        Assert.Equal(0, tiles[0].MinX);
        Assert.Equal(50, tiles[0].MaxX);
    }

    [Fact]
    public void Large_Sheet_Splits_With_Overlap()
    {
        // 100x100 sheet, 60x60 tiles, 10mm overlap → 2x2 tiles.
        var tiles = TilingEngine.Tile(0, 0, 100, 100, 60, 60, 10);
        Assert.Equal(4, tiles.Count);
        // Tiles overlap by 10mm along the seam (tile 0 spans 0..60, tile 1 starts at 50).
        Assert.Equal(60, tiles[0].MaxX);
        Assert.Equal(50, tiles[1].MinX);
        // Bottom-left tile spans X 0..60; bottom-right tile reaches the corner.
        Assert.Equal(60, tiles[2].MaxX);
        Assert.Equal(100, tiles[2].MaxY);
        Assert.Equal(100, tiles[3].MaxX);
        Assert.Equal(100, tiles[3].MaxY);
        // Full coverage: every corner is inside some tile.
        Assert.True(TilingEngine.Contains(tiles[0], 0, 0));
        Assert.True(TilingEngine.Contains(tiles[^1], 100, 100));
    }

    [Fact]
    public void Degenerate_Input_Returns_Empty()
    {
        Assert.Empty(TilingEngine.Tile(0, 0, 0, 0, 10, 10));
        Assert.Empty(TilingEngine.Tile(0, 0, 10, 10, 0, 10));
    }
}
