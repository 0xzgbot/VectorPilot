namespace VectorPilot.Engine;

public enum UnitSystem
{
    Inches,
    Millimeters
}

public static class UnitConversions
{
    public const double MmPerInch = 25.4;

    public static double ToInches(double value, UnitSystem from)
        => from == UnitSystem.Inches ? value : value / MmPerInch;

    public static double FromInches(double inches, UnitSystem to)
        => to == UnitSystem.Inches ? inches : inches * MmPerInch;

    public static string Suffix(UnitSystem u) => u == UnitSystem.Inches ? "in" : "mm";
}
