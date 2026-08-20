namespace VectorPilot.App;

/// <summary>How much of the tool set the UI offers.</summary>
public enum UiMode
{
    /// <summary>A short, approachable operation list. No Thread Mill, no rotary, no gadgets.</summary>
    Beginner,

    /// <summary>Everything in the registry.</summary>
    Advanced
}

/// <summary>What a job starter sets up for the user.</summary>
public enum JobStarterKind
{
    Sign,
    Photo,
    ThreeD
}

/// <summary>
/// Which operations each UI mode offers, and what each job starter selects.
///
/// The registry grew to 28 strategies, and the first thing a new user met was a combo box
/// containing "Thread Mill" and "Wrapped Fluting". Beginner mode trims that to operations
/// someone can actually choose between; Advanced is unchanged (the full registry).
///
/// This lives outside the panel so tests exercise the SAME filter the combo box uses,
/// rather than a copy of the rule.
/// </summary>
public static class UiModeCatalog
{
    /// <summary>
    /// The Beginner operation allow-list, in the order a beginner meets them: cut a shape
    /// out, hollow it, engrave it, drill it, then the 3D and laser basics.
    /// </summary>
    public static readonly string[] BeginnerKeys =
    {
        "profile",
        "pocket",
        "vcarve",
        "drill",
        "quickengrave",
        "rough3d",
        "finish3d",
        "laser-cut"
    };

    /// <summary>Hard ceiling for Beginner mode — the card's acceptance criterion.</summary>
    public const int BeginnerMaxOperations = 8;

    /// <summary>True when this registry key is offered in the given mode.</summary>
    public static bool IsVisible(UiMode mode, string? strategyKey)
        => mode == UiMode.Advanced
           || (strategyKey is not null && BeginnerKeys.Contains(strategyKey));

    /// <summary>
    /// Filter registry entries for a mode, preserving BeginnerKeys order in Beginner mode so
    /// the list reads sensibly instead of following registration order.
    /// </summary>
    public static List<T> Filter<T>(UiMode mode, IEnumerable<T> entries, Func<T, string> keyOf)
    {
        if (mode == UiMode.Advanced) return entries.ToList();

        var byKey = entries.ToDictionary(keyOf, e => e, StringComparer.Ordinal);
        var result = new List<T>();

        foreach (var key in BeginnerKeys)
            if (byKey.TryGetValue(key, out var entry))
                result.Add(entry);

        return result;
    }

    /// <summary>
    /// The strategy a job starter should pre-select, and the mode it implies. Sign and Photo
    /// are Beginner journeys; 3D needs the relief strategies, which are also in the Beginner
    /// list, so it stays Beginner too — Advanced is something the user opts into.
    /// </summary>
    public static (UiMode Mode, string StrategyKey) StarterSetup(JobStarterKind kind) => kind switch
    {
        JobStarterKind.Sign => (UiMode.Beginner, "vcarve"),
        JobStarterKind.Photo => (UiMode.Beginner, "photo-vcarve"),
        JobStarterKind.ThreeD => (UiMode.Beginner, "rough3d"),
        _ => (UiMode.Beginner, "profile")
    };

    /// <summary>Human label for a starter button.</summary>
    public static string Label(JobStarterKind kind) => kind switch
    {
        JobStarterKind.Sign => "Sign",
        JobStarterKind.Photo => "Photo",
        JobStarterKind.ThreeD => "3D Relief",
        _ => kind.ToString()
    };
}
