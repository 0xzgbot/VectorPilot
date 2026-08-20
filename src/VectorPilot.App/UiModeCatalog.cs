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

    /// <summary>Hard ceiling for Beginner mode — derived, so it cannot drift from the list.</summary>
    public static int BeginnerMaxOperations => BeginnerKeys.Length;

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
        return BeginnerKeys
            .Where(byKey.ContainsKey)
            .Select(k => byKey[k])
            .ToList();
    }

    /// <summary>
    /// The strategy a job starter pre-selects. The mode follows from the strategy —
    /// CutPanel.SelectStrategy promotes to Advanced when the key is not a Beginner
    /// operation — so this deliberately does not return one.
    /// </summary>
    public static string StarterStrategy(JobStarterKind kind) => kind switch
    {
        JobStarterKind.Sign => "vcarve",
        JobStarterKind.Photo => "photo-vcarve",
        JobStarterKind.ThreeD => "rough3d",
        _ => "profile"
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
