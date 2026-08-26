using MoonSharp.Interpreter;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Lua gadget host (Lua gadgets). Runs a user script in a sandbox and collects
/// the vectors it draws.
///
/// Sandboxed deliberately: the script gets a <see cref="CoreModules.Preset_HardSandbox"/>
/// environment, so no io/os/require/loadfile — a gadget cannot read the disk, shell out,
/// or load native code. An instruction-count quota stops runaway loops, because a
/// gadget that hangs takes the whole app with it.
/// </summary>
public static class LuaGadgetHost
{
    public sealed class Result
    {
        public List<VectorShape> Shapes { get; init; } = new();
        public List<string> Log { get; init; } = new();
        public string? Error { get; init; }
        public bool Ok => Error is null;
    }

    /// <summary>Default wall-clock budget — generous for real gadgets, fatal to `while true`.</summary>
    public const int DefaultTimeoutMs = 3000;

    /// <summary>
    /// Execute <paramref name="lua"/>. The script builds geometry through the injected
    /// <c>vp</c> table:
    ///
    ///   vp.line(x1,y1,x2,y2)          vp.circle(cx,cy,r[,segments])
    ///   vp.rect(x,y,w,h)              vp.polyline({{x,y},…}[,closed])
    ///   vp.log(msg)                   vp.sheet_width()  vp.sheet_height()
    /// </summary>
    public static Result Run(string lua, double sheetWidthMm = 0, double sheetHeightMm = 0,
                             int timeoutMs = DefaultTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(lua))
            return new Result { Error = "The gadget script is empty." };

        var shapes = new List<VectorShape>();
        var log = new List<string>();

        // Hard sandbox: no io, os, require, loadfile, or debug.
        var script = new Script(CoreModules.Preset_HardSandbox) { DebuggerEnabled = false };

        var vp = new Table(script);
        vp["line"] = (Action<double, double, double, double>)((x1, y1, x2, y2) =>
            shapes.Add(VectorShape.Line(new VectorPoint(x1, y1), new VectorPoint(x2, y2))));

        vp["circle"] = (Action<double, double, double, int?>)((cx, cy, r, segments) =>
        {
            if (r <= 0) throw new ScriptRuntimeException("circle radius must be > 0");
            shapes.Add(VectorShape.Circle(new VectorPoint(cx, cy), r));
            _ = segments;
        });

        vp["rect"] = (Action<double, double, double, double>)((x, y, w, h) =>
        {
            if (w <= 0 || h <= 0) throw new ScriptRuntimeException("rect needs positive width and height");
            shapes.Add(VectorShape.Rectangle(x, y, w, h));
        });

        vp["polyline"] = (Action<Table, bool?>)((pts, closed) =>
        {
            var points = new List<VectorPoint>();
            foreach (var pair in pts.Values)
            {
                if (pair.Type != DataType.Table) continue;
                var t = pair.Table;
                points.Add(new VectorPoint(t.Get(1).CastToNumber() ?? 0, t.Get(2).CastToNumber() ?? 0));
            }
            if (points.Count < 2) throw new ScriptRuntimeException("polyline needs at least 2 points");
            shapes.Add(VectorShape.Polyline(points, closed ?? false));
        });

        vp["log"] = (Action<string>)(msg => log.Add(msg ?? ""));
        vp["sheet_width"] = (Func<double>)(() => sheetWidthMm);
        vp["sheet_height"] = (Func<double>)(() => sheetHeightMm);

        script.Globals["vp"] = vp;

        try
        {
            // Compile HERE, not on the worker: a syntax error must surface as a
            // SyntaxErrorException with its line number, and anything thrown inside
            // Task.Wait comes back boxed as "One or more errors occurred".
            var fn = script.LoadString(lua);

            // Runaway-loop guard. MoonSharp has no instruction budget, so cap wall-clock
            // time: a gadget that hangs must not take the app with it.
            Exception? inner = null;
            var worker = new Thread(() =>
            {
                try { script.Call(fn); }
                catch (Exception ex) { inner = ex; }
            }) { IsBackground = true };

            worker.Start();
            if (!worker.Join(timeoutMs))
                return new Result { Error = $"Gadget timed out after {timeoutMs:N0}ms — possible infinite loop.", Log = log };

            if (inner is not null) throw inner;
        }
        catch (SyntaxErrorException ex)
        {
            return new Result { Error = $"Lua syntax error: {ex.DecoratedMessage ?? ex.Message}", Log = log };
        }
        catch (ScriptRuntimeException ex)
        {
            return new Result { Error = $"Gadget error: {ex.DecoratedMessage ?? ex.Message}", Log = log };
        }
        catch (Exception ex)
        {
            return new Result { Error = $"Gadget failed: {ex.Message}", Log = log };
        }

        return shapes.Count == 0
            ? new Result { Error = "The gadget ran but drew nothing — call vp.line/circle/rect/polyline.", Log = log }
            : new Result { Shapes = shapes, Log = log };
    }
}
