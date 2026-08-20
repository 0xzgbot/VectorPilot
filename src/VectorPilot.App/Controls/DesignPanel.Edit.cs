using System.Windows;
using System.Windows.Input;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

public partial class DesignPanel
{
    private void Undo_Click(object sender, RoutedEventArgs e) => DoUndo();
    private void Redo_Click(object sender, RoutedEventArgs e) => DoRedo();
    private void Delete_Click(object sender, RoutedEventArgs e) => DoDelete();
    private void Duplicate_Click(object sender, RoutedEventArgs e) => DoDuplicate();
    private void SelectAll_Click(object sender, RoutedEventArgs e) => DoSelectAll();
    private void FlipH_Click(object sender, RoutedEventArgs e) => DoFlip(horizontal: true);
    private void FlipV_Click(object sender, RoutedEventArgs e) => DoFlip(horizontal: false);

    private void Union_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Union);
    private void Subtract_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Subtract);
    private void Intersect_Click(object sender, RoutedEventArgs e) => DoBoolean(BooleanSelectionOps.Op.Intersect);

    private void Transform_Click(object sender, RoutedEventArgs e) => DoTransform();

    /// <summary>Card E1: import a bitmap, trace its outlines, add them as vectors.</summary>
    private void Trace_Click(object sender, RoutedEventArgs e)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked) { SetStatus("No editable layer"); return; }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Trace bitmap",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var (pixels, w, h) = LoadGrayscale(dlg.FileName);
            var traced = BitmapTracer.Trace(pixels, w, h, threshold: 128, simplifyTolerance: 1.0);
            if (traced.Count == 0) { SetStatus("Nothing traced — try a higher-contrast image"); return; }

            // Scale the trace to fit the sheet, preserving aspect.
            var sheet = AppState.CurrentJob.ActiveSheet;
            double sw = ParseDim(sheet.Width, 200), sh = ParseDim(sheet.Height, 200);
            double scale = Math.Min(sw / Math.Max(w, 1), sh / Math.Max(h, 1)) * 0.9;

            var before = UndoStack.Snapshot(layer);
            foreach (var shape in traced)
            {
                for (int i = 0; i < shape.Points.Count; i++)
                {
                    var p = shape.Points[i];
                    shape.Points[i] = new VectorPoint(p.X * scale, (h - p.Y) * scale);  // flip to CNC Y-up
                }
                layer.AddShape(shape);
            }
            Undo.Push("Trace bitmap", layer, before);
            if (AppState.CurrentJob is { } job) job.IsDirty = true;

            SetStatus($"Traced {traced.Count} outline(s) from {System.IO.Path.GetFileName(dlg.FileName)}");
            RedrawShapes();
            UpdateEditChrome();
        }
        catch (Exception ex)
        {
            SetStatus($"Trace failed: {ex.Message}");
        }
    }

    private static double ParseDim(object? value, double fallback) => value switch
    {
        double d => d,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => fallback
    };

    /// <summary>Decode an image to row-major 8-bit grayscale.</summary>
    private static (byte[] Pixels, int Width, int Height) LoadGrayscale(string path)
    {
        var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        var gray = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            frame, System.Windows.Media.PixelFormats.Gray8, null, 0);

        int w = gray.PixelWidth, h = gray.PixelHeight;
        int stride = w;
        var pixels = new byte[w * h];
        gray.CopyPixels(pixels, stride, 0);
        return (pixels, w, h);
    }

    private void TextureFill_Click(object sender, RoutedEventArgs e) => DoTextureFill();

    /// <summary>
    /// Fill the selected CLOSED shapes with a repeating pattern. VectorTextureEngine had no
    /// app call-site, so decorative fills existed but were unreachable.
    /// </summary>
    internal int DoTextureFill(VectorTextureEngine.PatternKind? patternOverride = null)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return 0; }
        if (Selection.IsEmpty) { SetStatus("Select a closed shape to fill"); return 0; }

        // A texture needs an enclosed region: an open path has no inside to fill.
        var closed = Selection.Selected
            .Where(s => s.Closed || s.Type == ShapeType.Circle || s.Type == ShapeType.Rectangle)
            .ToList();

        if (closed.Count == 0)
        {
            SetStatus("Texture fill needs a CLOSED shape — an open path has no inside. " +
                      "Close it, or use Extend to make the ends meet.");
            return 0;
        }

        var kind = patternOverride ?? ((CmbTexture.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string) switch
        {
            "Dots" => VectorTextureEngine.PatternKind.Dots,
            "Zigzag" => VectorTextureEngine.PatternKind.Zigzag,
            _ => VectorTextureEngine.PatternKind.Crosshatch
        };

        var children = VectorTextureEngine.Generate(closed, new VectorTextureEngine.Params
        {
            Pattern = kind,
            SpacingMm = 4.0,
            ClipToBoundary = true
        });

        if (children.Count == 0)
        {
            SetStatus($"{kind} produced no geometry — try a larger shape or tighter spacing");
            return 0;
        }

        var before = UndoStack.Snapshot(layer);
        foreach (var c in children) layer.AddShape(c);

        Undo.Push("Texture fill", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        SetStatus($"{kind} fill added {children.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
        return children.Count;
    }

    private void FitCurves_Click(object sender, RoutedEventArgs e) => DoFitCurves();

    /// <summary>
    /// Smooth the selected polylines. FitCurvesEngine had no app call-site, so imported
    /// DXF/traced geometry could never be cleaned up before cutting.
    /// </summary>
    internal int DoFitCurves(double? smoothingOverride = null)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return 0; }
        if (Selection.IsEmpty) { SetStatus("Select polylines to fit"); return 0; }

        // Opt in to decimation: smoothing alone only moves points, so without a tolerance
        // the button would report "fitted" while streaming exactly as many moves.
        var p = new FitCurvesParams
        {
            Smoothing = smoothingOverride ?? 0.5,
            SimplifyToleranceMm = 0.05
        };

        var before = UndoStack.Snapshot(layer);
        int changed = 0, removed = 0;

        foreach (var shape in Selection.Selected.ToList())
        {
            if (shape.Points.Count < 3) continue;

            var result = FitCurvesEngine.Fit(shape, p);
            if (result.Fitted.Count < 2) continue;

            removed += result.InputPointCount - result.OutputPointCount;
            shape.Points.Clear();
            shape.Points.AddRange(result.Fitted);
            changed++;
        }

        if (changed == 0)
        {
            SetStatus("Nothing to fit — select a polyline with at least three points");
            return 0;
        }

        Undo.Push("Fit curves", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        SetStatus(removed > 0
            ? $"Fitted {changed} shape(s) — {removed} point(s) removed"
            : $"Fitted {changed} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
        return changed;
    }

    private void TextOnCurve_Click(object sender, RoutedEventArgs e) => DoTextOnCurve();

    /// <summary>
    /// Lay the typed text along the selected path as outlines. TextOnCurve existed in
    /// VectorPilot.App with no XAML call-site, so no user could reach it.
    /// </summary>
    internal int DoTextOnCurve(string? textOverride = null)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return 0; }

        string text = textOverride ?? TxtCurveText.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Type the text to place, then select a path and press Text on curve");
            return 0;
        }

        var path = Selection.Selected.FirstOrDefault(s => s.Points.Count >= 2);
        if (path is null)
        {
            SetStatus("Select a path with at least two points to run the text along");
            return 0;
        }

        var glyphs = TextOnCurve.Place(text, path.Points);
        if (glyphs.Count == 0)
        {
            SetStatus($"\"{text}\" produced no outlines — try a longer path or a shorter string");
            return 0;
        }

        var before = UndoStack.Snapshot(layer);
        foreach (var g in glyphs) layer.AddShape(g);

        Undo.Push("Text on curve", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        SetStatus($"Placed \"{text}\" along the path — {glyphs.Count} outline(s)");
        RedrawShapes();
        UpdateEditChrome();
        return glyphs.Count;
    }

    private void Validate_Click(object sender, RoutedEventArgs e) => DoValidate();

    /// <summary>
    /// Report open vectors / self-intersections and SELECT the offenders so they are
    /// visible. VectorValidator had no app call-site, so these defects reached Calculate
    /// silently and produced junk G-code.
    /// </summary>
    internal int DoValidate()
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return 0; }

        var shapes = layer.Shapes.ToList();
        if (shapes.Count == 0)
        {
            ValidateLabel.Text = "";
            SetStatus("Nothing to validate — the layer is empty");
            return 0;
        }

        var issues = VectorValidator.Validate(shapes);

        if (issues.Count == 0)
        {
            ValidateLabel.Text = "✓ clean";
            ValidateLabel.Foreground = System.Windows.Media.Brushes.SeaGreen;
            SetStatus($"All {shapes.Count} shape(s) valid — no open vectors or self-intersections");
            return 0;
        }

        // Highlight the offenders by selecting them: that is the existing visual
        // affordance for "look at these".
        Selection.Clear();
        foreach (var idx in issues.Select(i => i.ShapeIndex).Distinct())
            if (idx >= 0 && idx < shapes.Count)
                Selection.Select(shapes[idx], additive: true);

        int errors = issues.Count(i => i.Severity == VectorIssueSeverity.Error);
        int warnings = issues.Count - errors;

        ValidateLabel.Text = errors > 0
            ? $"⚠ {errors} error(s), {warnings} warning(s)"
            : $"⚠ {warnings} warning(s)";
        ValidateLabel.Foreground = errors > 0
            ? System.Windows.Media.Brushes.Firebrick
            : System.Windows.Media.Brushes.DarkOrange;

        SetStatus($"{issues.Count} issue(s): " + string.Join("; ", issues.Take(3).Select(i => i.Message)));
        RedrawShapes();
        UpdateEditChrome();
        return issues.Count;
    }

    private void Fillet_Click(object sender, RoutedEventArgs e) => DoFillet();

    private void Extend_Click(object sender, RoutedEventArgs e) => DoExtend();

    /// <summary>
    /// Round the selected shapes' corners. ShapeFilletEngine lived in Geometry with no
    /// app call-site, so a user could not fillet anything.
    /// </summary>
    internal void DoFillet(double? radiusOverride = null)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return; }
        if (Selection.IsEmpty) { SetStatus("Select shapes to fillet"); return; }

        double radius = radiusOverride
            ?? (double.TryParse(TxtFilletRadius.Text, out var r) ? r : 5);
        if (radius <= 0) { SetStatus("Fillet radius must be greater than zero"); return; }

        var before = UndoStack.Snapshot(layer);
        int changed = 0;

        foreach (var shape in Selection.Selected.ToList())
        {
            if (shape.Points.Count < 3) continue;
            int pointsBefore = shape.Points.Count;

            var filleted = ShapeFilletEngine.Fillet(shape, radius);
            if (filleted.Points.Count == pointsBefore) continue;

            shape.Points.Clear();
            shape.Points.AddRange(filleted.Points);
            changed++;
        }

        if (changed == 0)
        {
            SetStatus($"No corner accepted a {radius:0.##}mm fillet — try a smaller radius");
            return;
        }

        Undo.Push("Fillet", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Filleted {changed} shape(s) at {radius:0.##}mm");
        RedrawShapes();
        UpdateEditChrome();
    }

    /// <summary>
    /// Extend open paths at both ends by the distance, so two paths that miss can be made
    /// to meet. ShapeExtendEngine had no app call-site either.
    /// </summary>
    internal void DoExtend(double? distanceOverride = null)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return; }
        if (Selection.IsEmpty) { SetStatus("Select open paths to extend"); return; }

        double distance = distanceOverride
            ?? (double.TryParse(TxtFilletRadius.Text, out var d) ? d : 5);
        if (distance <= 0) { SetStatus("Extend distance must be greater than zero"); return; }

        var before = UndoStack.Snapshot(layer);
        int changed = 0;

        foreach (var shape in Selection.Selected.ToList())
        {
            // A closed outline has no free ends to extend.
            if (shape.Closed || shape.Points.Count < 2) continue;

            var extended = ShapeExtendEngine.Extend(shape, distance);
            if (extended.Points.Count == 0) continue;

            shape.Points.Clear();
            shape.Points.AddRange(extended.Points);
            changed++;
        }

        if (changed == 0)
        {
            SetStatus("Nothing to extend — select an OPEN path (a closed outline has no ends)");
            return;
        }

        Undo.Push("Extend", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Extended {changed} path(s) by {distance:0.##}mm at each end");
        RedrawShapes();
        UpdateEditChrome();
    }

    private void Nest_Click(object sender, RoutedEventArgs e) => DoNest();

    /// <summary>
    /// Pack the selected closed shapes onto the sheet, undoably. NestingEngine shipped
    /// with ZERO app call-sites — it computed placements nobody ever applied.
    /// </summary>
    internal void DoNest(double spacingMm = 2.0)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null) { SetStatus("No active layer"); return; }

        if (Selection.IsEmpty) { SetStatus("Select closed shapes to nest"); return; }

        var sheet = AppState.CurrentJob!.ActiveSheet;
        var before = UndoStack.Snapshot(layer);

        var outcome = NestApply.Apply(
            Selection.Selected.ToList(), sheet.Width, sheet.Height, spacingMm);

        if (!outcome.Ok) { SetStatus(outcome.Error!); return; }

        Undo.Push("Nest", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        SetStatus(outcome.Unplaced > 0
            ? $"Nested {outcome.Placed} shape(s), {outcome.Unplaced} did not fit — {outcome.Utilization:P0} sheet use"
            : $"Nested {outcome.Placed} shape(s) — {outcome.Utilization:P0} sheet use");

        RedrawShapes();
        UpdateEditChrome();
    }

    /// <summary>Run a sandboxed Lua gadget and add whatever it draws to this layer.</summary>
    private void Gadget_Click(object sender, RoutedEventArgs e)
    {
        if (App.IsAutomated) return;   // no modals under automation
        var dlg = new GadgetDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) Refresh();
    }

    /// <summary>Shape grouping for this document (Mac UX-polish parity).</summary>
    internal readonly ShapeGroupModel Groups = new();

    /// <summary>Group the selection so members select together.</summary>
    private void Group_Click(object sender, RoutedEventArgs e) => DoGroup();

    private void Ungroup_Click(object sender, RoutedEventArgs e) => DoUngroup();

    internal void DoGroup()
    {
        if (Selection.Count < 2) { SetStatus("Select two or more shapes to group"); return; }

        var g = Groups.Group(Selection.Selected);
        SetStatus(g is null ? "Nothing to group" : $"{g.Name} — {g.ShapeIds.Count} shapes");
        UpdateEditChrome();
    }

    internal void DoUngroup()
    {
        if (Selection.IsEmpty) { SetStatus("Select a grouped shape to ungroup"); return; }

        int n = Groups.Ungroup(Selection.Selected);
        SetStatus(n == 0 ? "Selection is not grouped" : $"Ungrouped {n} group(s)");
        UpdateEditChrome();
    }

    /// <summary>Card P3: turn the selection's bounds into a keep-out zone.</summary>
    private void KeepOut_Click(object sender, RoutedEventArgs e)
    {
        if (Selection.IsEmpty) { SetStatus("Select shapes to mark as keep-out"); return; }

        if (Selection.SelectionBounds() is not { } b) { SetStatus("Selection has no area"); return; }

        var job = AppState.CurrentJob;
        job.KeepOutZones.Add(new KeepOutZone
        {
            Name = $"Zone {job.KeepOutZones.Count + 1}",
            Type = KeepOutZoneType.Rectangle,
            RectMinX = b.MinX,
            RectMinY = b.MinY,
            RectMaxX = b.MaxX,
            RectMaxY = b.MaxY,
            IsActive = true
        });
        job.IsDirty = true;

        SetStatus($"Keep-out zone added ({b.MaxX - b.MinX:F1} × {b.MaxY - b.MinY:F1}) — {job.KeepOutZones.Count} total");
        RedrawShapes();
    }

    /// <summary>Card A3: open the numeric transform dialog, undoably.</summary>
    internal void DoTransform()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty)
        {
            SetStatus("Select at least one shape first");
            return;
        }

        var before = UndoStack.Snapshot(layer);
        var dlg = new TransformDialog(Selection.Selected.ToList())
        {
            Owner = Window.GetWindow(this)
        };
        dlg.ShowDialog();
        if (!dlg.Applied) { SetStatus("Transform cancelled"); return; }

        Undo.Push("Transform", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Transformed {Selection.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    /// <summary>Card A2: boolean-combine the selection, undoably.</summary>
    internal void DoBoolean(BooleanSelectionOps.Op op)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked) return;
        if (!BooleanSelectionOps.CanApply(Selection.Selected))
        {
            SetStatus("Select 2+ closed shapes first");
            return;
        }

        var before = UndoStack.Snapshot(layer);
        var made = BooleanSelectionOps.Apply(layer, Selection.Selected, op);
        Undo.Push($"{op}", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;

        Selection.Clear();
        foreach (var s in made) Selection.Select(s, additive: true);
        SetStatus(made.Count == 0 ? $"{op}: empty result" : $"{op} → {made.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoUndo()
    {
        var label = Undo.Undo();
        Selection.Clear();
        SetStatus(label is null ? "Nothing to undo" : $"Undid: {label}");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoRedo()
    {
        var label = Undo.Redo();
        Selection.Clear();
        SetStatus(label is null ? "Nothing to redo" : $"Redid: {label}");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoDelete()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        int n = Selection.Count;
        Selection.DeleteSelected(layer);
        Undo.Push($"Delete {n} shape(s)", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Deleted {n} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoDuplicate()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        int n = Selection.Count;
        double off = Math.Max(layer.Shapes.Count > 0 ? 5 : 5, WorldTolerance(8));
        Selection.DuplicateSelected(layer, off, off);
        Undo.Push($"Duplicate {n} shape(s)", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Duplicated {n} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoSelectAll()
    {
        var layer = ActiveLayer;
        if (layer is null) return;
        Selection.SelectAll(layer);
        SetStatus($"{Selection.Count} shape(s) selected");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void DoFlip(bool horizontal)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var b = Selection.SelectionBounds();
        if (b is null) return;

        var before = UndoStack.Snapshot(layer);
        double cx = (b.Value.MinX + b.Value.MaxX) / 2;
        double cy = (b.Value.MinY + b.Value.MaxY) / 2;

        foreach (var shape in Selection.Selected)
        {
            for (int i = 0; i < shape.Points.Count; i++)
            {
                var p = shape.Points[i];
                shape.Points[i] = horizontal
                    ? new VectorPoint(2 * cx - p.X, p.Y)
                    : new VectorPoint(p.X, 2 * cy - p.Y);
            }
        }

        Undo.Push(horizontal ? "Flip Horizontal" : "Flip Vertical", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus(horizontal ? "Flipped horizontally" : "Flipped vertically");
        RedrawShapes();
        UpdateEditChrome();
    }

    internal void NudgeSelection(double dx, double dy)
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || Selection.IsEmpty) return;
        var before = UndoStack.Snapshot(layer);
        Selection.MoveSelected(dx, dy);
        Undo.Push("Nudge", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus($"Nudged {Selection.Count} shape(s)");
        RedrawShapes();
        UpdateEditChrome();
    }

    /// <summary>Card A1: delete the grabbed node, undoably.</summary>
    internal void DeleteSelectedNodeWithUndo()
    {
        var layer = ActiveLayer;
        if (layer is null || layer.Locked || !NodeEdit.IsActive) return;

        var before = UndoStack.Snapshot(layer);
        if (!NodeEdit.DeleteSelectedNode())
        {
            SetStatus("Cannot delete — shape is at its minimum point count");
            return;
        }
        Undo.Push("Delete node", layer, before);
        if (AppState.CurrentJob is { } job) job.IsDirty = true;
        SetStatus("Node deleted");
        RedrawShapes();
        UpdateEditChrome();
    }

    private void DesignPanel_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        double step = shift ? 10 : 1;

        switch (e.Key)
        {
            case Key.Z when ctrl: DoUndo(); e.Handled = true; break;
            case Key.Y when ctrl: DoRedo(); e.Handled = true; break;
            case Key.D when ctrl: DoDuplicate(); e.Handled = true; break;
            case Key.A when ctrl: DoSelectAll(); e.Handled = true; break;
            case Key.G when ctrl && shift: DoUngroup(); e.Handled = true; break;
            case Key.G when ctrl: DoGroup(); e.Handled = true; break;
            case Key.Delete or Key.Back:
                if (NodeEdit.IsActive && NodeEdit.HasSelectedNode) DeleteSelectedNodeWithUndo();
                else DoDelete();
                e.Handled = true;
                break;
            case Key.Escape:
                if (NodeEdit.IsActive)
                {
                    NodeEdit.Exit();
                    SetStatus("Exited node mode");
                }
                else
                {
                    _polylinePoints.Clear();
                    Selection.Clear();
                    SetStatus("Cleared selection");
                }
                RedrawShapes(); UpdateEditChrome();
                e.Handled = true;
                break;
            case Key.Left: NudgeSelection(-step, 0); e.Handled = true; break;
            case Key.Right: NudgeSelection(step, 0); e.Handled = true; break;
            case Key.Up: NudgeSelection(0, -step); e.Handled = true; break;
            case Key.Down: NudgeSelection(0, step); e.Handled = true; break;
        }
    }
}
