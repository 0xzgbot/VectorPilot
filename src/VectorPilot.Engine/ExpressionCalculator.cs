using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>Document variable (ported from DocumentVariable.swift, SPK-0512).</summary>
public sealed class DocumentVariable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Category { get; set; } = "General";
}

/// <summary>Driven dimension (computed value from an expression; SPK-0512).</summary>
public sealed class DrivenDimension
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Expression { get; set; } = "";
    public string Category { get; set; } = "Dimensions";
}

/// <summary>
/// Public numeric-expression evaluator backing calculation edit boxes
/// (SPK-0209). Supports + − × ÷, parentheses, decimal numbers, named
/// variables ($width / bare width), and π. Unknown letters → nil (hardened:
/// unresolved variables error instead of silently parsing as a different number).
/// </summary>
public static class ExpressionCalculator
{
    public static double? Evaluate(string expression, IReadOnlyList<DocumentVariable>? variables = null)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0) return null;

        var varMap = new Dictionary<string, double>(StringComparer.Ordinal);
        if (variables is not null)
        {
            foreach (var v in variables)
            {
                if (double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    varMap[v.Key] = num;
                }
            }
        }

        // Longest keys first so $width is substituted before width.
        var processed = trimmed;
        foreach (var name in varMap.Keys.OrderByDescending(k => k.Length))
        {
            processed = processed.Replace("$" + name, varMap[name].ToString(CultureInfo.InvariantCulture));
            processed = processed.Replace(name, varMap[name].ToString(CultureInfo.InvariantCulture));
        }
        processed = processed.Replace("π", Math.PI.ToString(CultureInfo.InvariantCulture));
        processed = processed.Replace("pi", Math.PI.ToString(CultureInfo.InvariantCulture));

        // Hardening: any leftover letters mean an unresolved variable — error, don't skip.
        if (processed.Any(char.IsLetter)) return null;

        var evaluator = new ExpressionEvaluator(processed);
        var result = evaluator.Evaluate();
        return result is { } r && double.IsFinite(r) ? r : null;
    }

    /// <summary>Resolve a driven dimension's expression against document variables.</summary>
    public static double? Resolve(DrivenDimension dimension, IReadOnlyList<DocumentVariable> variables)
        => Evaluate(dimension.Expression, variables);
}

/// <summary>Recursive-descent numeric evaluator (mirrors the Swift ExpressionEvaluator).</summary>
internal sealed class ExpressionEvaluator
{
    private readonly string _text;
    private int _pos;

    public ExpressionEvaluator(string text) => _text = text;

    public double? Evaluate()
    {
        SkipWhitespace();
        var result = ParseExpression();
        SkipWhitespace();
        if (_pos < _text.Length) return null;
        return double.IsFinite(result) ? result : null;
    }

    private double ParseExpression()
    {
        var result = ParseTerm();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _text.Length) break;
            var ch = _text[_pos];
            if (ch != '+' && ch != '-') break;
            _pos++;
            SkipWhitespace();
            var right = ParseTerm();
            result = ch == '+' ? result + right : result - right;
        }
        return result;
    }

    private double ParseTerm()
    {
        var result = ParseFactor();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _text.Length) break;
            var ch = _text[_pos];
            if (ch != '*' && ch != '/') break;
            _pos++;
            SkipWhitespace();
            var right = ParseFactor();
            if (ch == '*') result *= right;
            else if (Math.Abs(right) < 1e-15) return double.NaN; // division by zero → non-finite
            else result /= right;
        }
        return result;
    }

    private double ParseFactor()
    {
        SkipWhitespace();
        if (_pos >= _text.Length) return double.NaN;

        var ch = _text[_pos];
        if (ch == '-') { _pos++; return -ParseFactor(); }
        if (ch == '+') { _pos++; return ParseFactor(); }
        if (ch == '(')
        {
            _pos++;
            var result = ParseExpression();
            SkipWhitespace();
            if (_pos >= _text.Length || _text[_pos] != ')') return double.NaN;
            _pos++;
            return result;
        }

        if (char.IsDigit(ch) || ch == '.')
        {
            var num = new System.Text.StringBuilder();
            int dots = 0;
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (char.IsDigit(c)) { num.Append(c); _pos++; }
                else if (c == '.' && dots < 1) { num.Append(c); dots++; _pos++; }
                else break;
            }
            return double.TryParse(num.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }

        // Unknown character → error (ExpressionCalculator pre-filters letters).
        return double.NaN;
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
    }
}
