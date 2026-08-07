namespace VectorPilot.Engine;

/// <summary>Material definition (mirrors ShopPilot Material/MaterialSetup).</summary>
public sealed class Material
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Generic";
    public System.Drawing.Color? DisplayColor { get; set; }
    public double? RecommendedFeedRate { get; set; }
    public double? RecommendedPlungeRate { get; set; }
    public double? RecommendedSpindleSpeed { get; set; }

    public static Material Pine() => new() { Name = "Pine", DisplayColor = System.Drawing.Color.FromArgb(0xD8, 0xC0, 0x8A), RecommendedFeedRate = 120, RecommendedSpindleSpeed = 16000 };
    public static Material Oak() => new() { Name = "Oak", DisplayColor = System.Drawing.Color.FromArgb(0xB0, 0x8A, 0x5A), RecommendedFeedRate = 100, RecommendedSpindleSpeed = 14000 };
    public static Material MDF() => new() { Name = "MDF", DisplayColor = System.Drawing.Color.FromArgb(0xC8, 0xB8, 0xA0), RecommendedFeedRate = 140, RecommendedSpindleSpeed = 18000 };
    public static Material Plywood() => new() { Name = "Plywood", DisplayColor = System.Drawing.Color.FromArgb(0xB8, 0xA0, 0x78), RecommendedFeedRate = 110, RecommendedSpindleSpeed = 16000 };
    public static Material Acrylic() => new() { Name = "Acrylic", DisplayColor = System.Drawing.Color.FromArgb(0x90, 0xD0, 0xF0), RecommendedFeedRate = 60, RecommendedSpindleSpeed = 12000 };
    public static Material Aluminum6061() => new() { Name = "Aluminum 6061", DisplayColor = System.Drawing.Color.FromArgb(0xC0, 0xC0, 0xC8), RecommendedFeedRate = 40, RecommendedSpindleSpeed = 10000 };
    public static Material Steel() => new() { Name = "Steel", DisplayColor = System.Drawing.Color.FromArgb(0x88, 0x88, 0x90), RecommendedFeedRate = 20, RecommendedSpindleSpeed = 6000 };
}
