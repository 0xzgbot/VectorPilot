using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathPersistenceTests
{
    [Fact]
    public void Round_Trip_Preserves_Fields()
    {
        var tp = new Toolpath
        {
            Name = "Outline",
            Strategy = ToolpathStrategy.Profile,
            CutDepth = 3.5,
            FeedRate = 1000,
            SpindleSpeed = 12000,
            IsDirty = false
        };
        tp.GCode.AddRange(new[] { "G0 X0 Y0", "G1 X10 Y0 F1000", "M30" });

        var persisted = ToolpathPersistence.ToPersisted(tp);
        var back = ToolpathPersistence.FromPersisted(persisted);

        Assert.Equal(tp.Id, back.Id);
        Assert.Equal("Outline", back.Name);
        Assert.Equal(ToolpathStrategy.Profile, back.Strategy);
        Assert.Equal(3.5, back.CutDepth);
        Assert.Equal(1000, back.FeedRate);
        Assert.Equal(12000, back.SpindleSpeed);
        Assert.Equal(tp.GCode, back.GCode);
    }

    [Fact]
    public void Invalid_Guids_Fall_Back_To_Defaults()
    {
        var back = ToolpathPersistence.FromPersisted(new VectorPilot.Engine.IO.PersistedToolpath
        {
            Id = "not-a-guid",
            Name = "X",
            Strategy = "pocket"
        });
        Assert.NotEqual(Guid.Empty, back.Id);
        Assert.Equal(ToolpathStrategy.Pocket, back.Strategy);
    }
}
