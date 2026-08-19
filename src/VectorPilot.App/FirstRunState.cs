using System.IO;

namespace VectorPilot.App;

/// <summary>
/// First-run state (Mac SPK-UXPOLISH parity). A marker file in LocalAppData
/// records that the welcome screen has been shown, so it appears once — and the
/// user can suppress it explicitly. Pure file logic, kept out of the dialog so it
/// is testable without WPF.
/// </summary>
public sealed class FirstRunState
{
    private readonly string _markerPath;

    public FirstRunState(string? markerPath = null)
    {
        _markerPath = markerPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorPilot", "first-run.marker");
    }

    /// <summary>True when the welcome screen has not been dismissed yet.</summary>
    public bool IsFirstRun
    {
        get
        {
            try { return !File.Exists(_markerPath); }
            catch (IOException) { return false; }   // unreadable: do not nag
        }
    }

    /// <summary>Record that the welcome screen was shown and should not reappear.</summary>
    public void MarkShown()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            // Failing to persist just means the welcome shows again — never fatal.
        }
    }

    /// <summary>Forget the marker so the welcome screen shows again (for testing/support).</summary>
    public void Reset()
    {
        try { if (File.Exists(_markerPath)) File.Delete(_markerPath); }
        catch (IOException) { }
    }
}
