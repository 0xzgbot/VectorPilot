using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class DocumentVariablesTests
{
    [Fact]
    public void Add_Update_Delete()
    {
        var m = new DocumentVariablesModel(Path.Combine(Path.GetTempPath(), $"dv-{Guid.NewGuid():N}.json"));
        m.AddVariable("material", "MDF", "Stock");
        m.AddVariable("width", "1219", "Stock");
        Assert.Equal(2, m.Variables.Count);
        var id = m.Variables[0].Id;
        Assert.True(m.UpdateVariable(id, "material", "Plywood"));
        Assert.Equal("Plywood", m.Variables[0].Value);
        Assert.True(m.DeleteVariable(id));
        Assert.Single(m.Variables);
    }

    [Fact]
    public void Binding_Resolves_Variable_Value()
    {
        var m = new DocumentVariablesModel(Path.Combine(Path.GetTempPath(), $"dv-{Guid.NewGuid():N}.json"));
        m.AddVariable("width", "1219");
        m.AddBinding("width", "sheetWidth", "Sheet");
        Assert.Equal(1219, m.GetResolvedValue("sheetWidth", "Sheet"));
    }

    [Fact]
    public void Binding_Fallback_When_Variable_Missing()
    {
        var m = new DocumentVariablesModel(Path.Combine(Path.GetTempPath(), $"dv-{Guid.NewGuid():N}.json"));
        m.AddBinding("missing", "sheetWidth", "Sheet", 500);
        Assert.Equal(500, m.GetResolvedValue("sheetWidth", "Sheet"));
    }

    [Fact]
    public void Save_Load_Round_Trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dv-{Guid.NewGuid():N}.json");
        var m = new DocumentVariablesModel(path);
        m.AddVariable("material", "Oak", "Stock");
        m.AddVariable("note", "Test", "General");
        m.Save();
        var m2 = new DocumentVariablesModel(path);
        Assert.True(m2.Load());
        Assert.Equal(2, m2.Variables.Count);
        Assert.Equal("Oak", m2.Variables.First(v => v.Key == "material").Value);
    }
}
