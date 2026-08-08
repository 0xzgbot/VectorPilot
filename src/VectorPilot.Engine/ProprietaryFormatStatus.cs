namespace VectorPilot.Engine;

/// <summary>
/// Honest status for vendor-proprietary formats that need external SDKs/specs.
/// These are NOT silently faked — each reports its true capability so the UI
/// can surface the right message ("install SketchUp SDK", "OpenNURBS pending", etc.).
/// </summary>
public static class ProprietaryFormatStatus
{
    /// <summary>
    /// V3M (Aspire 3D clipart) is a proprietary binary format with no public spec.
    /// The Mac reference app does not import V3M either. Stub until a spec or
    /// sample corpus exists.
    /// </summary>
    public const string V3m = "not-implemented: V3M is a proprietary binary format without a public specification. " +
                              "No V3M importer exists in the Mac reference app either. Implement when a spec/samples are available.";

    /// <summary>
    /// SKP (SketchUp) needs SketchUpAPI.dll (proprietary SDK). Aspire ships the
    /// 9MB SketchUpAPI.dll next to the SKP importer. We do not bundle third-party
    /// binaries; the import surface exists and will wire to the SDK when present.
    /// </summary>
    public const string Skp = "not-implemented: SKP import requires SketchUpAPI.dll (proprietary SketchUp SDK). " +
                              "Aspire bundles it (9MB); we do not ship third-party binaries. Import surface reserved.";

    /// <summary>
    /// 3DM (Rhino) is documented by the OpenNURBS SDK (open source). A full port
    /// is a standalone project; a minimal mesh-only parser can follow. Tracked.
    /// </summary>
    public const string ThreeDm = "pending: 3DM import can be built from the OpenNURBS spec (open source). " +
                                  "Tracked as a follow-up item; no guessed binary layout shipped.";

    public static bool IsImplemented(string status) => status.StartsWith("implemented");
}

/// <summary>Registry of known-unsupported imports (UI surfaces these honestly).</summary>
public static class ImportCapabilities
{
    public static IReadOnlyList<(string Format, string Status)> Unsupported => new[]
    {
        ("V3M 3D Clipart", ProprietaryFormatStatus.V3m),
        ("SKP (SketchUp)", ProprietaryFormatStatus.Skp),
        ("3DM (Rhino)", ProprietaryFormatStatus.ThreeDm),
    };
}
