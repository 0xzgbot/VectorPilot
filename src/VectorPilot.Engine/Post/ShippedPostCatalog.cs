namespace VectorPilot.Engine;

/// <summary>
/// Shipped post-processor catalog (card E3). Data only — every entry uses the
/// existing <see cref="PostTemplate"/> grammar and is expanded by
/// <see cref="PostTemplateEngine"/>. Controllers differ mainly in preamble,
/// spindle handling, coolant, and end-of-program words.
/// </summary>
public static class ShippedPostCatalog
{
    /// <summary>Build a template from a controller definition.</summary>
    private static PostTemplate Make(
        string id, string name, string summary,
        string unitsCode, string unitsComment,
        string preamble, string postamble,
        bool rotary = false) => new()
    {
        Id = id,
        Name = name,
        Summary = summary,
        RotaryWrap = rotary,
        Text = $"""
            %
            (VectorPilot post: {name})
            [N|A|N|3.0] {unitsCode} ; {unitsComment}
            [N|A|N|3.0] G90 ; Absolute positioning
            [N|A|N|3.0] G17 ; XY plane
            {preamble}
            (--- moves ---)
            [N|A|N|3.0] [G]
            (--- end ---)
            {postamble}
            %
            """
    };

    private const string Mm = "G21";
    private const string In = "G20";

    // ---- FluidNC: GRBL-compatible, supports G54 work offsets explicitly ----
    public static PostTemplate FluidNc(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "fluidnc-mm" : "fluidnc-in",
        units == GCodeUnits.Millimeter ? "FluidNC mm" : "FluidNC inch",
        "FluidNC (ESP32) — GRBL-compatible dialect",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        "[N|A|N|3.0] G54 ; Work coordinate system 1",
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] G0 Z5.000 ; Retract
        [N|A|N|3.0] M30 ; Program end and rewind
        """);

    // ---- Marlin: firmware retraction differs; M400 waits for moves ----
    public static PostTemplate Marlin(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "marlin-mm" : "marlin-in",
        units == GCodeUnits.Millimeter ? "Marlin mm" : "Marlin inch",
        "Marlin 2.x CNC mode",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        "[N|A|N|3.0] M400 ; Wait for planner to drain",
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] M400 ; Wait for moves
        [N|A|N|3.0] G0 Z5.000 ; Retract
        [N|A|N|3.0] M2 ; Program end
        """);

    // ---- LinuxCNC: full G-code, uses M9 coolant off and %-delimited files ----
    public static PostTemplate LinuxCnc(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "linuxcnc-mm" : "linuxcnc-in",
        units == GCodeUnits.Millimeter ? "LinuxCNC mm" : "LinuxCNC inch",
        "LinuxCNC / EMC2 — RS274NGC",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        """
        [N|A|N|3.0] G40 ; Cancel cutter compensation
        [N|A|N|3.0] G49 ; Cancel tool length offset
        [N|A|N|3.0] G54 ; Work offset 1
        """,
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] M9 ; Coolant off
        [N|A|N|3.0] G0 Z5.000 ; Retract
        [N|A|N|3.0] M30 ; End
        """);

    // ---- Mach3 / Mach4: G53 retract, explicit tool change block ----
    public static PostTemplate Mach(int version, GCodeUnits units) => Make(
        $"mach{version}-{(units == GCodeUnits.Millimeter ? "mm" : "in")}",
        $"Mach{version} {(units == GCodeUnits.Millimeter ? "mm" : "inch")}",
        $"Mach{version} — Windows CNC controller",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        """
        [N|A|N|3.0] G40 ; Cancel compensation
        [N|A|N|3.0] G80 ; Cancel canned cycles
        """,
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] M9 ; Coolant off
        [N|A|N|3.0] G53 G0 Z0 ; Retract in machine coords
        [N|A|N|3.0] M30 ; End
        """);

    // ---- Shapeoko (Carbide Motion / GRBL): needs a dwell after spindle start ----
    public static PostTemplate Shapeoko(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "shapeoko-mm" : "shapeoko-in",
        units == GCodeUnits.Millimeter ? "Shapeoko mm" : "Shapeoko inch",
        "Shapeoko / Carbide Motion — GRBL",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        "[N|A|N|3.0] G4 P2.0 ; Dwell for spindle spin-up",
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] G0 Z5.000 ; Retract
        [N|A|N|3.0] M30 ; End
        """);

    // ---- Onefinity (Buildbotics): GRBL-class, homes before the program ----
    public static PostTemplate Onefinity(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "onefinity-mm" : "onefinity-in",
        units == GCodeUnits.Millimeter ? "Onefinity mm" : "Onefinity inch",
        "Onefinity / Buildbotics controller",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        "[N|A|N|3.0] G54 ; Work offset 1",
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] G0 Z5.000 ; Retract
        [N|A|N|3.0] M2 ; End
        """);

    // ---- Avid CNC (PRO series, typically Mach-based) ----
    public static PostTemplate Avid(GCodeUnits units) => Make(
        units == GCodeUnits.Millimeter ? "avid-mm" : "avid-in",
        units == GCodeUnits.Millimeter ? "Avid CNC mm" : "Avid CNC inch",
        "Avid CNC PRO — Mach-compatible",
        units == GCodeUnits.Millimeter ? Mm : In,
        units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units",
        """
        [N|A|N|3.0] G40 ; Cancel compensation
        [N|A|N|3.0] G49 ; Cancel length offset
        """,
        """
        [N|A|N|3.0] M5 ; Spindle off
        [N|A|N|3.0] G53 G0 Z0 ; Retract in machine coords
        [N|A|N|3.0] M30 ; End
        """);

    /// <summary>Rotary wrap variant for a GRBL-class controller (Y → A degrees).</summary>
    public static PostTemplate FluidNcRotary(double diameterMm = 50.0) => new()
    {
        Id = "fluidnc-rotary-y2a",
        Name = "FluidNC Rotary Wrap (Y2A)",
        Summary = $"FluidNC — Y → A degrees about X (wrap Ø{diameterMm:0} mm)",
        RotaryWrap = true,
        WrapDiameterMm = diameterMm,
        Text = """
            %
            (VectorPilot post: FluidNC rotary wrap Y2A)
            [N|A|N|3.0] G21 ; Millimeter units
            [N|A|N|3.0] G90 ; Absolute positioning
            [N|A|N|3.0] G17 ; XY plane
            [N|A|N|3.0] G54 ; Work offset 1
            (Y maps to A degrees about X — wrap diameter [D|A|-|1.1] mm)
            (--- moves ---)
            [N|A|N|3.0] [G]
            (--- end ---)
            [N|A|N|3.0] M5 ; Spindle off
            [N|A|N|3.0] G0 Z5.000 ; Retract
            [N|A|N|3.0] M30 ; End
            %
            """
    };

    /// <summary>Every catalog entry beyond the three built into PostTemplate.</summary>
    public static IReadOnlyList<PostTemplate> Additional { get; } = new List<PostTemplate>
    {
        FluidNc(GCodeUnits.Millimeter), FluidNc(GCodeUnits.Inch),
        Marlin(GCodeUnits.Millimeter),  Marlin(GCodeUnits.Inch),
        LinuxCnc(GCodeUnits.Millimeter), LinuxCnc(GCodeUnits.Inch),
        Mach(3, GCodeUnits.Millimeter), Mach(3, GCodeUnits.Inch),
        Mach(4, GCodeUnits.Millimeter), Mach(4, GCodeUnits.Inch),
        Shapeoko(GCodeUnits.Millimeter), Shapeoko(GCodeUnits.Inch),
        Onefinity(GCodeUnits.Millimeter), Onefinity(GCodeUnits.Inch),
        Avid(GCodeUnits.Millimeter), Avid(GCodeUnits.Inch),
        FluidNcRotary()
    };
}
