using System.IO;
using VectorPilot.App;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>ViewModel-only tests for the document variables panel (SPK-0512); no XAML involved.</summary>
public class DocumentVariablesPanelTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"vp-docvars-{Guid.NewGuid():N}.json");

    [Fact]
    public void AddVariable_And_Json_RoundTrip()
    {
        var path = TempPath();
        try
        {
            var vm = new DocumentVariablesViewModel(path);
            vm.AddVariable("stockWidth", "600", "General");
            vm.AddVariable("materialThickness", "18");
            vm.Save();

            Assert.True(File.Exists(path));

            var reloaded = new DocumentVariablesViewModel(path);
            Assert.Equal(2, reloaded.Variables.Count);
            Assert.Equal("stockWidth", reloaded.Variables[0].Key);
            Assert.Equal("600", reloaded.Variables[0].Value);
            Assert.Equal("General", reloaded.Variables[0].Category);
            Assert.Equal("materialThickness", reloaded.Variables[1].Key);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PreviewExpression_Resolves_Variables()
    {
        var vm = new DocumentVariablesViewModel(TempPath());
        vm.AddVariable("stockWidth", "600");
        Assert.Equal("300", vm.PreviewExpression("stockWidth / 2"));
    }

    [Fact]
    public void PreviewExpression_Unresolved_Returns_Invalid()
    {
        var vm = new DocumentVariablesViewModel(TempPath());
        Assert.Equal("invalid expression", vm.PreviewExpression("missing / 2"));

        // Key mismatch: "width" is not a variable, only "stockWidth" — must not partially substitute.
        vm.AddVariable("stockWidth", "600");
        Assert.Equal("invalid expression", vm.PreviewExpression("width / 2"));
    }

    [Fact]
    public void AddDimension_And_Json_RoundTrip()
    {
        var path = TempPath();
        try
        {
            var vm = new DocumentVariablesViewModel(path);
            vm.AddDimension("halfWidth", "stockWidth / 2", "Dimensions");
            vm.Save();

            var reloaded = new DocumentVariablesViewModel(path);
            var dim = Assert.Single(reloaded.Dimensions);
            Assert.Equal("halfWidth", dim.Key);
            Assert.Equal("stockWidth / 2", dim.Expression);
            Assert.Equal("Dimensions", dim.Category);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
