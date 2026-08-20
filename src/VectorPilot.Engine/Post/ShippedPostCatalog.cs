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

    // ---- industrial / machining-centre controllers ----

    /// <summary>Haas: G20/G21, M8 coolant, G53 park, M30 rewind.</summary>
    public static PostTemplate Haas(GCodeUnits u) => Make(
        $"haas-{Suffix(u)}", $"Haas {UnitName(u)}", "Haas VF/Mini-Mill machining centre",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G49 G80 ; cancel comp/length/canned\n            [N|A|N|3.0] G54 ; work offset 1",
        "[N|A|N|3.0] M9 ; coolant off\n            [N|A|N|3.0] G53 G0 Z0 ; park Z\n            [N|A|N|3.0] M30 ; end and rewind");

    /// <summary>Fanuc 0i / 30i — the dialect most industrial posts descend from.</summary>
    public static PostTemplate Fanuc(GCodeUnits u) => Make(
        $"fanuc-{Suffix(u)}", $"Fanuc {UnitName(u)}", "Fanuc 0i/30i machining centre",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G49 G80\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G91 G28 Z0 ; home Z\n            [N|A|N|3.0] M30");

    /// <summary>Siemens SINUMERIK 840D.</summary>
    public static PostTemplate Siemens(GCodeUnits u) => Make(
        $"sinumerik-{Suffix(u)}", $"SINUMERIK 840D {UnitName(u)}", "Siemens SINUMERIK 840D",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G64 ; continuous path mode\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z200 ; retract\n            [N|A|N|3.0] M30");

    /// <summary>Heidenhain TNC in ISO mode.</summary>
    public static PostTemplate Heidenhain(GCodeUnits u) => Make(
        $"heidenhain-{Suffix(u)}", $"Heidenhain TNC {UnitName(u)}", "Heidenhain TNC (ISO mode)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z250\n            [N|A|N|3.0] M30");

    /// <summary>Okuma OSP.</summary>
    public static PostTemplate Okuma(GCodeUnits u) => Make(
        $"okuma-{Suffix(u)}", $"Okuma OSP {UnitName(u)}", "Okuma OSP machining centre",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G80\n            [N|A|N|3.0] G15 H1 ; work offset",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M30");

    /// <summary>Centroid Acorn/Oak router and mill controls.</summary>
    public static PostTemplate Centroid(GCodeUnits u) => Make(
        $"centroid-{Suffix(u)}", $"Centroid {UnitName(u)}", "Centroid Acorn/Oak",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G80\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M30");

    // ---- CNC router / hobby controllers ----

    /// <summary>WinCNC — common on Camaster and Laguna routers.</summary>
    public static PostTemplate WinCnc(GCodeUnits u) => Make(
        $"wincnc-{Suffix(u)}", $"WinCNC {UnitName(u)}", "WinCNC router control",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M2");

    /// <summary>Masso G3 touch controllers.</summary>
    public static PostTemplate Masso(GCodeUnits u) => Make(
        $"masso-{Suffix(u)}", $"Masso G3 {UnitName(u)}", "Masso G3 touch CNC",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G80\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M30");

    /// <summary>UCCNC — Warp9 UC100/UC300 motion controllers.</summary>
    public static PostTemplate UcCnc(GCodeUnits u) => Make(
        $"uccnc-{Suffix(u)}", $"UCCNC {UnitName(u)}", "UCCNC (Warp9 UC100/UC300)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40 G49 G80\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M30");

    /// <summary>PlanetCNC controllers.</summary>
    public static PostTemplate PlanetCnc(GCodeUnits u) => Make(
        $"planetcnc-{Suffix(u)}", $"PlanetCNC {UnitName(u)}", "PlanetCNC Mk3/Mk3.4",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M9\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M2");

    /// <summary>Smoothieware — 3-axis router firmware.</summary>
    public static PostTemplate Smoothie(GCodeUnits u) => Make(
        $"smoothie-{Suffix(u)}", $"Smoothieware {UnitName(u)}", "Smoothieware router firmware",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G92 E0 ; reset extrude counter",
        "[N|A|N|3.0] M5 ; spindle off\n            [N|A|N|3.0] G0 Z5\n            [N|A|N|3.0] M2");

    /// <summary>Duet / RepRapFirmware in CNC mode.</summary>
    public static PostTemplate Duet(GCodeUnits u) => Make(
        $"duet-{Suffix(u)}", $"Duet RRF {UnitName(u)}", "Duet 3 / RepRapFirmware CNC mode",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] M453 ; CNC mode",
        "[N|A|N|3.0] M5\n            [N|A|N|3.0] G0 Z5\n            [N|A|N|3.0] M2 ; end program (M0 only PAUSES — the job would hang)");

    /// <summary>Shopbot in G-code (not OpenSBP) mode.</summary>
    public static PostTemplate ShopBot(GCodeUnits u) => Make(
        $"shopbot-{Suffix(u)}", $"ShopBot {UnitName(u)}", "ShopBot (G-code mode)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40\n            [N|A|N|3.0] G54",
        "[N|A|N|3.0] M5\n            [N|A|N|3.0] G0 Z0\n            [N|A|N|3.0] M2");

    /// <summary>X-Carve / Inventables machines running GRBL with a spindle relay.</summary>
    public static PostTemplate XCarve(GCodeUnits u) => Make(
        $"xcarve-{Suffix(u)}", $"X-Carve {UnitName(u)}", "Inventables X-Carve (GRBL)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G94 ; units per minute",
        "[N|A|N|3.0] M5\n            [N|A|N|3.0] G0 Z5\n            [N|A|N|3.0] M30");

    /// <summary>Longmill / Sienci — GRBL with a laser-safe preamble.</summary>
    public static PostTemplate LongMill(GCodeUnits u) => Make(
        $"longmill-{Suffix(u)}", $"LongMill {UnitName(u)}", "Sienci LongMill (GRBL)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G94",
        "[N|A|N|3.0] M5\n            [N|A|N|3.0] G0 Z5\n            [N|A|N|3.0] M30");

    // ---- laser / plasma ----

    /// <summary>GRBL-LPC laser: M4 dynamic power, S as power not RPM.</summary>
    public static PostTemplate GrblLaser(GCodeUnits u) => Make(
        $"grbl-laser-{Suffix(u)}", $"GRBL Laser {UnitName(u)}", "GRBL-LPC laser (M4 dynamic power)",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] M4 ; dynamic laser power\n            [N|A|N|3.0] G94",
        "[N|A|N|3.0] M5 ; laser off\n            [N|A|N|3.0] G0 X0 Y0\n            [N|A|N|3.0] M2");

    /// <summary>Plasma with torch height control.</summary>
    public static PostTemplate Plasma(GCodeUnits u) => Make(
        $"plasma-{Suffix(u)}", $"Plasma THC {UnitName(u)}", "Plasma cutter with torch height control",
        UnitsCode(u), UnitName(u),
        "[N|A|N|3.0] G40\n            [N|A|N|3.0] M65 P2 ; THC enable",
        "[N|A|N|3.0] M65 P3 ; torch off\n            [N|A|N|3.0] G0 Z25\n            [N|A|N|3.0] M30");

    private static string Suffix(GCodeUnits u) => u == GCodeUnits.Inch ? "in" : "mm";
    private static string UnitName(GCodeUnits u) => u == GCodeUnits.Inch ? "inch" : "mm";
    private static string UnitsCode(GCodeUnits u) => u == GCodeUnits.Inch ? "G20" : "G21";

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
        FluidNcRotary(),

        // Industrial machining centres.
        Haas(GCodeUnits.Millimeter), Haas(GCodeUnits.Inch),
        Fanuc(GCodeUnits.Millimeter), Fanuc(GCodeUnits.Inch),
        Siemens(GCodeUnits.Millimeter), Siemens(GCodeUnits.Inch),
        Heidenhain(GCodeUnits.Millimeter), Heidenhain(GCodeUnits.Inch),
        Okuma(GCodeUnits.Millimeter), Okuma(GCodeUnits.Inch),
        Centroid(GCodeUnits.Millimeter), Centroid(GCodeUnits.Inch),

        // Router / hobby controls.
        WinCnc(GCodeUnits.Millimeter), WinCnc(GCodeUnits.Inch),
        Masso(GCodeUnits.Millimeter), Masso(GCodeUnits.Inch),
        UcCnc(GCodeUnits.Millimeter), UcCnc(GCodeUnits.Inch),
        PlanetCnc(GCodeUnits.Millimeter), PlanetCnc(GCodeUnits.Inch),
        Smoothie(GCodeUnits.Millimeter), Smoothie(GCodeUnits.Inch),
        Duet(GCodeUnits.Millimeter), Duet(GCodeUnits.Inch),
        ShopBot(GCodeUnits.Millimeter), ShopBot(GCodeUnits.Inch),
        XCarve(GCodeUnits.Millimeter), XCarve(GCodeUnits.Inch),
        LongMill(GCodeUnits.Millimeter), LongMill(GCodeUnits.Inch),

        // Laser / plasma.
        GrblLaser(GCodeUnits.Millimeter), GrblLaser(GCodeUnits.Inch),
        Plasma(GCodeUnits.Millimeter), Plasma(GCodeUnits.Inch)
    };
}
