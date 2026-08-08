using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class PreferencesStoreTests
{
    [Fact]
    public void Defaults_And_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-prefs-{Guid.NewGuid():N}.json");
        try
        {
            var store = new PreferencesStore(path);
            Assert.Equal("mm", store.Value.Units);
            Assert.True(store.Value.ShowGrid);

            store.Update(p => { p.Units = "inch"; p.Theme = "Light"; p.AutosaveIntervalSeconds = 60; });
            var reloaded = new PreferencesStore(path);
            Assert.Equal("inch", reloaded.Value.Units);
            Assert.Equal("Light", reloaded.Value.Theme);
            Assert.Equal(60, reloaded.Value.AutosaveIntervalSeconds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-prefs-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{nope");
            var store = new PreferencesStore(path);
            Assert.Equal("mm", store.Value.Units);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class AutosaveManagerTests
{
    private static (string job, string auto, AutosaveManager mgr) NewManager()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vp-auto-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var job = Path.Combine(dir, "job.shoppilot");
        var mgr = new AutosaveManager(job, TimeSpan.FromMinutes(5));
        return (job, job + ".autosave", mgr);
    }

    [Fact]
    public void Autosave_Newer_Than_Job_Is_Recoverable()
    {
        var (job, auto, mgr) = NewManager();
        try
        {
            File.WriteAllText(job, "manual");
            Thread.Sleep(20);
            mgr.Save("autosaved");

            Assert.True(mgr.HasRecoverableWork());
            Assert.Equal("autosaved", mgr.ReadAutosave());
            mgr.Recover();
            Assert.Equal("autosaved", File.ReadAllText(job));
            mgr.Clear();
            Assert.False(File.Exists(auto));
            Assert.False(mgr.HasRecoverableWork());
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(job))) Directory.Delete(Path.GetDirectoryName(job)!, true);
        }
    }

    [Fact]
    public void No_Autosave_Means_No_Recovery()
    {
        var (job, _, mgr) = NewManager();
        try
        {
            File.WriteAllText(job, "manual");
            Assert.False(mgr.HasRecoverableWork());
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(job))) Directory.Delete(Path.GetDirectoryName(job)!, true);
        }
    }
}
