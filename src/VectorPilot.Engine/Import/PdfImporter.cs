using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Parses PDF vector content streams into <see cref="VectorShape"/> values.
/// Ported from ShopPilotGeometry.PDFImporter + PDFImporterParser (Swift).
/// </summary>
/// <remarks>
/// Honest lean slice: parses the path operators (<c>m l c v y h re</c>), the
/// painting operators (<c>S s f F B b</c>), graphics state (<c>q Q cm</c>) and
/// FlateDecode streams. Text, images and complex shadings are skipped
/// tolerantly (they are not cuttable vectors anyway). Plain (uncompressed)
/// content streams are legal PDFs — a stream is inflated only when its object
/// dict advertises <c>/FlateDecode</c> or the payload starts with zlib magic,
/// and inflation failures fall back to the raw payload. Parsing never throws.
/// </remarks>
public static class PdfImporter
{
    private const NumberStyles NumStyle = NumberStyles.Float;

    private static readonly Regex OperatorRegex = new("^[a-zA-Z*'\"]+$", RegexOptions.Compiled);

    /// <summary>Parse PDF text into shapes. Never throws; returns empty for non-PDF or garbage input.</summary>
    public static List<VectorShape> Parse(string content)
    {
        var shapes = new List<VectorShape>();
        if (content is null || content.Length < 5 || !content.StartsWith("%PDF", StringComparison.Ordinal))
            return shapes;

        foreach (var streamText in ExtractContentStreams(content))
        {
            shapes.AddRange(ParseContentStream(streamText));
        }
        return shapes;
    }

    // MARK: - Stream extraction

    /// <summary>
    /// Pulls every <c>stream … endstream</c> blob from the PDF text. Stream
    /// payloads are treated as Latin-1 text (every byte maps 1:1 to a char), so
    /// compressed payloads survive the string round-trip and can be inflated.
    /// </summary>
    private static List<string> ExtractContentStreams(string content)
    {
        var streams = new List<string>();
        var index = 0;
        while (true)
        {
            var streamIdx = content.IndexOf("stream", index, StringComparison.Ordinal);
            if (streamIdx < 0) break;

            // Now positioned just after "stream"; skip its EOL.
            var start = streamIdx + "stream".Length;
            while (start < content.Length && (content[start] == '\r' || content[start] == '\n')) start++;

            // Capture everything up to "endstream" (exclusive).
            var endIdx = content.IndexOf("endstream", start, StringComparison.Ordinal);
            if (endIdx < 0) break;

            var payload = content.Substring(start, endIdx - start);
            // Strip the trailing EOL before endstream.
            payload = payload.TrimEnd('\r', '\n');

            // Loose dict look-back: does the object header advertise FlateDecode?
            var dictStart = Math.Max(0, streamIdx - 300);
            var dict = content.Substring(dictStart, streamIdx - dictStart);
            var wantsInflate = dict.Contains("/FlateDecode", StringComparison.Ordinal) || LooksLikeZlib(payload);

            streams.Add(wantsInflate ? Inflate(payload) ?? payload : payload);
            index = endIdx + "endstream".Length;
        }
        return streams;
    }

    /// <summary>zlib magic: 0x78 0x01 (no/low), 0x78 0x9C (default), 0x78 0xDA (best).</summary>
    private static bool LooksLikeZlib(string payload)
    {
        if (payload.Length < 2) return false;
        var b0 = (byte)payload[0];
        var b1 = (byte)payload[1];
        return b0 == 0x78 && (b1 == 0x01 || b1 == 0x9C || b1 == 0xDA);
    }

    /// <summary>RFC-1950 zlib inflate via <see cref="ZLibStream"/>; null when not inflatable.</summary>
    private static string? Inflate(string payload)
    {
        try
        {
            var bytes = Encoding.Latin1.GetBytes(payload);
            using var input = new MemoryStream(bytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var inflated = output.ToArray();
            if (inflated.Length == 0) return null;
            return Encoding.ASCII.GetString(inflated);
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Content-stream parser (operators → VectorShape)

    private enum TokenKind { Number, Operator, Other }

    /// <summary>
    /// Parse one (inflated) content stream into shapes. Tracks the CTM stack
    /// (<c>cm</c>), the current subpath, and closes/emits on painting operators.
    /// </summary>
    private static List<VectorShape> ParseContentStream(string text)
    {
        var shapes = new List<VectorShape>();

        // Current path state (in user space, transformed by the CTM at emit).
        var currentPoints = new List<VectorPoint>();
        VectorPoint? subpathStart = null;
        VectorPoint? currentPoint = null;

        var ctmStack = new List<double[,]> { IdentityMatrix };
        var ctm = IdentityMatrix;

        var tokens = Tokenize(text);
        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Operator) { i += 1; continue; }

            switch (token.Text)
            {
                case "m":
                case "l":
                    if (ArgPoint(tokens, i - 2, ctm) is VectorPoint pt)
                    {
                        if (token.Text == "m")
                        {
                            currentPoints = new List<VectorPoint> { pt };
                            subpathStart = pt;
                        }
                        else if (currentPoints.Count > 0)
                        {
                            currentPoints.Add(pt);
                        }
                        currentPoint = pt;
                    }
                    i += 1;
                    break;

                case "c":
                case "v":
                case "y":
                {
                    // Cubic Bezier → sample into the current subpath.
                    var args = ArgPoints(tokens, i, token.Text, ctm);
                    if (args.Count == 3 && currentPoint is VectorPoint cp0 && currentPoints.Count > 0)
                    {
                        currentPoints.AddRange(SampleBezier(cp0, args[0], args[1], args[2], 12));
                        currentPoint = args[2];
                    }
                    i += 1;
                    break;
                }

                case "re":
                    if (ArgRect(tokens, i, ctm) is VectorShape rect)
                    {
                        shapes.Add(rect);
                    }
                    i += 1;
                    break;

                case "h":
                    if (currentPoints.Count > 0 && subpathStart is VectorPoint start)
                    {
                        currentPoints.Add(start);
                        currentPoint = start;
                    }
                    i += 1;
                    break;

                case "S":
                case "s":
                case "f":
                case "F":
                case "B":
                case "b":
                case "n":
                    // Painting operator: emit the accumulated subpath (if any).
                    if (currentPoints.Count >= 2)
                    {
                        var first = currentPoints[0];
                        var last = currentPoints[^1];
                        var closed = Math.Abs(first.X - last.X) < 1e-9 && Math.Abs(first.Y - last.Y) < 1e-9;
                        shapes.Add(VectorShape.Polyline(currentPoints, closed));
                    }
                    currentPoints = new List<VectorPoint>();
                    subpathStart = null;
                    currentPoint = null;
                    i += 1;
                    break;

                case "q":
                    ctmStack.Add(ctm);
                    i += 1;
                    break;

                case "Q":
                    if (ctmStack.Count > 1)
                    {
                        ctm = ctmStack[^1];
                        ctmStack.RemoveAt(ctmStack.Count - 1);
                    }
                    i += 1;
                    break;

                case "cm":
                    if (ArgMatrix(tokens, i) is double[,] m)
                    {
                        ctm = Multiply(m, ctm);
                    }
                    i += 1;
                    break;

                default:
                    // Text ops (BT ET Td TD Tm T* Tj TJ ' ") and anything else are skipped.
                    i += 1;
                    break;
            }
        }
        return shapes;
    }

    // MARK: - Tokenizer

    private static List<(TokenKind Kind, double Number, string Text)> Tokenize(string text)
    {
        var tokens = new List<(TokenKind, double, string)>();
        var current = new StringBuilder();
        var inLiteralString = false;
        var inHexString = false;

        void Flush()
        {
            var trimmed = current.ToString().Trim();
            current.Clear();
            if (trimmed.Length == 0) return;
            if (double.TryParse(trimmed, NumStyle, CultureInfo.InvariantCulture, out var n))
                tokens.Add((TokenKind.Number, n, string.Empty));
            else if (OperatorRegex.IsMatch(trimmed))
                tokens.Add((TokenKind.Operator, 0, trimmed));
            else
                tokens.Add((TokenKind.Other, 0, trimmed));
        }

        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (inLiteralString)
            {
                // A backslash escapes the next character (skip it).
                if (ch == '\\' && index + 1 < text.Length) index += 1;
                else if (ch == ')') inLiteralString = false;
                index += 1;
                continue;
            }
            if (inHexString)
            {
                if (ch == '>') inHexString = false;
                index += 1;
                continue;
            }
            switch (ch)
            {
                case '(':
                case '[':
                    inLiteralString = true;
                    Flush();
                    break;
                case '<':
                    inHexString = true;
                    Flush();
                    break;
                case ']':
                case ')':
                case '>':
                    Flush();
                    break;
                case ' ':
                case '\n':
                case '\r':
                case '\t':
                    Flush();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
            index += 1;
        }
        Flush();
        return tokens;
    }

    // MARK: - Args

    private static readonly double[,] IdentityMatrix = new double[3, 3]
    {
        { 1, 0, 0 },
        { 0, 1, 0 },
        { 0, 0, 1 }
    };

    /// <summary>Point from the two numbers before an <c>m</c>/<c>l</c> at token index <paramref name="i"/>.</summary>
    private static VectorPoint? ArgPoint(List<(TokenKind Kind, double Number, string Text)> tokens, int i, double[,] ctm)
    {
        if (i < 0 || i + 1 >= tokens.Count) return null;
        if (tokens[i].Kind != TokenKind.Number || tokens[i + 1].Kind != TokenKind.Number) return null;
        return Transform(tokens[i].Number, tokens[i + 1].Number, ctm);
    }

    /// <summary>Three control points for <c>c</c>/<c>v</c>/<c>y</c> (operand count differs per op).</summary>
    private static List<VectorPoint> ArgPoints(
        List<(TokenKind Kind, double Number, string Text)> tokens, int i, string op, double[,] ctm)
    {
        var operandCount = op == "c" ? 6 : 4;
        if (i - operandCount < 0) return new List<VectorPoint>();
        var pts = new List<VectorPoint>();
        var idx = i - operandCount;
        while (idx < i)
        {
            if (idx + 1 < tokens.Count
                && tokens[idx].Kind == TokenKind.Number
                && tokens[idx + 1].Kind == TokenKind.Number)
            {
                pts.Add(Transform(tokens[idx].Number, tokens[idx + 1].Number, ctm));
            }
            else
            {
                return new List<VectorPoint>();
            }
            idx += 2;
        }
        return pts;
    }

    /// <summary>Rectangle <c>re</c>: x y w h.</summary>
    private static VectorShape? ArgRect(
        List<(TokenKind Kind, double Number, string Text)> tokens, int i, double[,] ctm)
    {
        if (i - 4 < 0) return null;
        if (tokens[i - 4].Kind != TokenKind.Number || tokens[i - 3].Kind != TokenKind.Number
            || tokens[i - 2].Kind != TokenKind.Number || tokens[i - 1].Kind != TokenKind.Number)
        {
            return null;
        }
        var x = tokens[i - 4].Number;
        var y = tokens[i - 3].Number;
        var w = tokens[i - 2].Number;
        var h = tokens[i - 1].Number;
        var a = Transform(x, y, ctm);
        var b = Transform(x + w, y + h, ctm);
        return VectorShape.Rectangle(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    /// <summary>Transformation matrix <c>cm</c>: a b c d e f (PDF row-major convention).</summary>
    private static double[,]? ArgMatrix(List<(TokenKind Kind, double Number, string Text)> tokens, int i)
    {
        if (i - 6 < 0) return null;
        var vals = new double[6];
        var idx = i - 6;
        for (var k = 0; k < 6; k++, idx++)
        {
            if (tokens[idx].Kind != TokenKind.Number) return null;
            vals[k] = tokens[idx].Number;
        }
        return new double[3, 3]
        {
            { vals[0], vals[1], 0 },
            { vals[2], vals[3], 0 },
            { vals[4], vals[5], 1 }
        };
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var out_ = new double[3, 3];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var sum = 0.0;
                for (var k = 0; k < 3; k++) sum += a[r, k] * b[k, c];
                out_[r, c] = sum;
            }
        }
        return out_;
    }

    private static VectorPoint Transform(double x, double y, double[,] ctm)
    {
        var wx = ctm[0, 0] * x + ctm[1, 0] * y + ctm[2, 0];
        var wy = ctm[0, 1] * x + ctm[1, 1] * y + ctm[2, 1];
        return new VectorPoint(wx, wy);
    }

    /// <summary>Sample a cubic Bezier into <paramref name="steps"/> points (excluding the start point).</summary>
    private static List<VectorPoint> SampleBezier(
        VectorPoint p0, VectorPoint p1, VectorPoint p2, VectorPoint p3, int steps)
    {
        var pts = new List<VectorPoint>();
        for (var s = 1; s <= steps; s++)
        {
            var t = (double)s / steps;
            var u = 1 - t;
            var x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            var y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
            pts.Add(new VectorPoint(x, y));
        }
        return pts;
    }
}
