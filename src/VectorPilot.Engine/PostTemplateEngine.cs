using System.Globalization;
using System.Text.RegularExpressions;

namespace VectorPilot.Engine;

// GCodeUnits / GCodeUnitsInfo are defined in Post/GRBLPostProcessor.cs.

/// <summary>
/// Post template (ported from PostTemplate.swift, SPK-1134): a text recipe
/// that turns raw move lines into machine G-code. Grammar: [WORD|MODE|OUT|FORMAT]
/// — WORD (X/Y/Z/A/F/S/T/D/N…), MODE (A absolute · C current · I incremental),
/// OUT (emitted letter or "-" to suppress), FORMAT (w.d decimals).
/// </summary>
public sealed class PostTemplate
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Text { get; init; } = "";
    public bool RotaryWrap { get; init; }
    public double WrapDiameterMm { get; init; } = 50.0;

    /// <summary>
    /// What the user (and UIAutomation) sees. Without this the post picker reports
    /// "VectorPilot.Engine.PostTemplate" for all 20 entries, so they are
    /// indistinguishable to a screen reader or any automation.
    /// </summary>
    public override string ToString() => Name;

    public static PostTemplate Grbl(GCodeUnits units) => new()
    {
        Id = units == GCodeUnits.Millimeter ? "grbl-mm" : "grbl-in",
        Name = units == GCodeUnits.Millimeter ? "GRBL mm" : "GRBL inch",
        Summary = units == GCodeUnits.Millimeter ? "GRBL 1.1 — metric (G21)" : "GRBL 1.1 — imperial (G20)",
        Text = GrblTemplateText(units)
    };

    public static PostTemplate GrblRotaryWrap(double diameterMm = 50.0) => new()
    {
        Id = "grbl-rotary-y2a",
        Name = "GRBL Rotary Wrap (Y2A)",
        Summary = $"Y → A degrees about X (wrap diameter {diameterMm:0} mm)",
        Text = RotaryWrapTemplateText,
        RotaryWrap = true,
        WrapDiameterMm = diameterMm
    };

    /// <summary>
    /// Every shipped post: the three GRBL built-ins plus the controller catalog
    /// in <see cref="ShippedPostCatalog"/> (card E3).
    /// </summary>
    public static readonly List<PostTemplate> Shipped = BuildShipped();

    private static List<PostTemplate> BuildShipped()
    {
        var list = new List<PostTemplate>
        {
            Grbl(GCodeUnits.Millimeter),
            Grbl(GCodeUnits.Inch),
            GrblRotaryWrap()
        };
        list.AddRange(ShippedPostCatalog.Additional);
        return list;
    }

    public static PostTemplate? ShippedById(string id) => Shipped.FirstOrDefault(t => t.Id == id);

    private static string GrblTemplateText(GCodeUnits units)
    {
        var modal = GCodeUnitsInfo.ModalCode(units);
        var unitsComment = units == GCodeUnits.Millimeter ? "Millimeter units" : "Inch units";
        return $"""
            %
            (VectorPilot {(units == GCodeUnits.Millimeter ? "mm" : "in")} post)
            [N|A|N|3.0] {modal} ; {unitsComment}
            [N|A|N|3.0] G90 ; Absolute positioning
            [N|A|N|3.0] G17 ; XY plane
            (--- moves ---)
            [N|A|N|3.0] [G]
            (--- end ---)
            [N|A|N|3.0] M9
            [N|A|N|3.0] G0 Z5.000 ; Retract to safe height
            [N|A|N|3.0] M2
            %
            """;
    }

    private const string RotaryWrapTemplateText = """
        %
        (VectorPilot GRBL rotary wrap Y2A post)
        [N|A|N|3.0] G21 ; Millimeter units
        [N|A|N|3.0] G90 ; Absolute positioning
        [N|A|N|3.0] G17 ; XY plane
        (Y maps to A degrees about X — wrap diameter [D|A|-|1.1] mm)
        (--- moves ---)
        [N|A|N|3.0] [G]
        (--- end ---)
        [N|A|N|3.0] M9
        [N|A|N|3.0] G0 Z5.000 ; Retract to safe height
        [N|A|N|3.0] M2
        %
        """;
}

/// <summary>
/// Expands PostTemplate recipes over raw move lines (ported from
/// PostTemplateEngine.swift, SPK-1134). Header before (--- moves ---), move
/// templates between the markers, footer after. Rotary wrap converts Y→A.
/// </summary>
public static class PostTemplateEngine
{
    public sealed class EmitResult
    {
        public List<string> Lines { get; init; } = new();
        public int MoveCount { get; init; }
    }

    private const string MovesMarker = "(--- moves ---)";
    private const string EndMarker = "(--- end ---)";

    private static readonly Regex LineNumberRegex = new(@"\[N\|A\|N\|[0-9]\.[0-9]\]", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"\[([A-Za-z])\|([ACI])\|(-|[A-Za-z])\|([0-9]\.[0-9])\]", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"([A-Za-z])([+-]?\d*\.?\d+)", RegexOptions.Compiled);

    public static EmitResult Emit(IReadOnlyList<string> gcodeLines, PostTemplate template)
    {
        var recipeLines = template.Text.Split('\n');
        var header = new List<string>();
        var moveTemplates = new List<string>();
        var footer = new List<string>();
        int section = 0;
        foreach (var line in recipeLines)
        {
            var trimmed = line.Trim();
            if (trimmed == MovesMarker) { section = 1; continue; }
            if (trimmed == EndMarker) { section = 2; continue; }
            switch (section)
            {
                case 0: header.Add(line); break;
                case 1: moveTemplates.Add(line); break;
                default: footer.Add(line); break;
            }
        }

        var out_ = new List<string>();
        int lineNumber = 10;
        var lastWords = new Dictionary<string, double>();
        int moveCount = 0;

        void EmitRecipeLine(string recipe, ParsedMove? move)
        {
            var expanded = recipe;
            if (expanded.Contains("[N|A|N|"))
            {
                expanded = LineNumberRegex.Replace(expanded, $"N{lineNumber}");
                lineNumber += 10;
            }
            expanded = move is { } m
                ? ExpandMove(expanded, m, template, lastWords)
                : ExpandNonMove(expanded, template, lastWords);
            out_.Add(expanded);
        }

        foreach (var line in header) EmitRecipeLine(line, null);

        foreach (var raw in gcodeLines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('(') || trimmed == "%" || trimmed.StartsWith("O="))
            {
                out_.Add(trimmed);
                continue;
            }
            if (ParseMove(trimmed) is not { } parsed)
            {
                out_.Add(trimmed);
                continue;
            }
            moveCount++;
            foreach (var mt in moveTemplates) EmitRecipeLine(mt, parsed);
        }

        foreach (var line in footer) EmitRecipeLine(line, null);

        var cleaned = out_.Select(l => Regex.Replace(l, "  +", " ").Trim()).ToList();
        return new EmitResult { Lines = cleaned, MoveCount = moveCount };
    }

    private sealed class ParsedMove
    {
        public string Command { get; init; } = "";
        public Dictionary<string, double> Words { get; init; } = new();
    }

    private static ParsedMove? ParseMove(string line)
    {
        string? command = null;
        var words = new Dictionary<string, double>();
        foreach (Match m in WordRegex.Matches(line))
        {
            var letter = m.Groups[1].Value.ToUpperInvariant();
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) continue;
            if (letter is "G" or "M")
            {
                command ??= $"{letter}{(int)num}";
            }
            else
            {
                words[letter] = num;
            }
        }
        return command is null ? null : new ParsedMove { Command = command, Words = words };
    }

    private static readonly string[] WordOrder = { "X", "Y", "Z", "A", "B", "C", "F", "S", "T" };

    private static string FullLine(ParsedMove move, PostTemplate template)
    {
        var parts = new List<string> { move.Command };
        foreach (var word in WordOrder)
        {
            if (!move.Words.TryGetValue(word, out var v)) continue;
            var printed = WrappedValue(v, word, template);
            var emittedWord = template.RotaryWrap && word == "Y" ? "A" : word;
            int decimals = word is "F" or "S" or "T" ? 0 : 3;
            parts.Add($"{emittedWord}{printed.ToString("F" + decimals, CultureInfo.InvariantCulture)}");
        }
        return string.Join(" ", parts);
    }

    private static double WrappedValue(double v, string word, PostTemplate template)
        => template.RotaryWrap && word == "Y" ? v / (Math.PI * template.WrapDiameterMm) * 360.0 : v;

    private static string ExpandMove(string recipe, ParsedMove move, PostTemplate template, Dictionary<string, double> words)
    {
        var hasTokens = TokenRegex.IsMatch(recipe);
        var gReplacement = hasTokens ? move.Command : FullLine(move, template);
        var expanded = recipe.Replace("[G]", gReplacement);
        if (!hasTokens)
        {
            foreach (var (word, value) in move.Words) words[word] = value;
            return expanded;
        }

        return TokenRegex.Replace(expanded, m =>
        {
            var word = m.Groups[1].Value.ToUpperInvariant();
            var mode = m.Groups[2].Value;
            var outLetter = m.Groups[3].Value;
            var format = m.Groups[4].Value;
            return ExpandToken(word, mode, outLetter, format, move, template, words) ?? "";
        });
    }

    private static string? ExpandToken(string word, string mode, string outLetter, string format,
        ParsedMove move, PostTemplate template, Dictionary<string, double> words)
    {
        double? value = word switch
        {
            "G" => null,
            "D" => template.WrapDiameterMm,
            _ => move.Words.TryGetValue(word, out var v) ? v : null
        };
        if (word == "G") return move.Command;
        if (value is not { } raw) return null;

        double displayValue;
        switch (mode)
        {
            case "C":
                if (words.TryGetValue(word, out var last) && Math.Abs(last - raw) < 1e-9) return null;
                displayValue = raw;
                break;
            case "I":
                displayValue = raw - (words.TryGetValue(word, out var prev) ? prev : 0);
                break;
            default:
                displayValue = raw;
                break;
        }
        words[word] = raw;

        var printed = WrappedValue(displayValue, word, template);
        int decimals = int.TryParse(format.Split('.')[^1], out var d) ? d : 3;
        var formatted = printed.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return outLetter == "-" ? formatted : outLetter + formatted;
    }

    private static string ExpandNonMove(string recipe, PostTemplate template, Dictionary<string, double> words)
    {
        return TokenRegex.Replace(recipe, m =>
        {
            var word = m.Groups[1].Value.ToUpperInvariant();
            var mode = m.Groups[2].Value;
            var outLetter = m.Groups[3].Value;
            var format = m.Groups[4].Value;
            if (word == "D")
            {
                int decimals = int.TryParse(format.Split('.')[^1], out var d) ? d : 1;
                return template.WrapDiameterMm.ToString("F" + decimals, CultureInfo.InvariantCulture);
            }
            if (mode == "C" && words.TryGetValue(word, out var last))
            {
                int decimals = int.TryParse(format.Split('.')[^1], out var d) ? d : 3;
                var printed = WrappedValue(last, word, template);
                var formatted = printed.ToString("F" + decimals, CultureInfo.InvariantCulture);
                return outLetter == "-" ? formatted : outLetter + formatted;
            }
            return "";
        });
    }
}
