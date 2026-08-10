# VectorPilot ↔ Aspire V12.5 Parity Matrix

Source of truth: `ASPIRE_LIVE_CAPTURE.md` (live-capture ground truth, Aspire
Trial 12.5 installed at `C:\Program Files\Aspire Trial Edition 12.5`).
Generated 2026-08-10. Status key: ✅ parity, 🟡 partial, ❌ absent, ⛔ blocked.

## Feature surface (Aspire's 8 critical parity items)

| Aspire surface | VectorPilot | Status |
|---|---|---|
| 17 toolpath strategies | 20 in the strategy registry (Profile, Pocket, V-Carve, Drill, Drill Bank, Quick Engrave, Photo V-Carve, Sketch Carve, Rough 3D, Finish 3D, Texture, Drag Knife, Prism, Fluting, Chamfer, Bevel, Sweep, Moulding, Weave, Wrapped Fluting, Laser, Rotary Wrap) | ✅ superset |
| 7 tool types (End Mill, V-Bit, Ball Nose, Drill, Diamond Drag, Laser, Thread Mill) | 10 in ToolType (adds RadiusedEndMill, Engraving, RadiusedEngraving) | ✅ superset |
| 53+ post processors, 5 categories | 3 shipped GRBL templates (mm/in/rotary-Y2A) + template engine ([W\|M\|O\|F] grammar, arbitrary user templates) | 🟡 catalog small, engine capable |
| Gadget system (Lua + HTML) | Keyhole gadget engine (SPK-0907); no Lua/HTML gadget host | 🟡 |
| Cabinetry import (CSV PartListMapping, 5 transformation types) | CabinetryImport.cs (Mozaik/KCD/CabinetSense/CabinetPartsPro/Polyboard/SmartWOP) | 🟡 needs fixture validation |
| 3D preview (OSG camera/shaded/AA) | WPF Viewport3D: heightfield mesh, toolpath overlay, ghost diff, playback transport | 🟡 no camera animation |
| SketchUp .skp import | honest stub — needs SketchUpAPI.dll | ⛔ SDK-blocked |
| Output files `.tap` | TapExporter (.tap) + dirty-export gate + post-template path | ✅ |

## Toolpath strategies (17 vs 20)

Profile ✅ · Pocket ✅ · V-Carve ✅ · V-Carve Inlay ✅ (recipe presets 30/45/60/90)
· Drilling ✅ · Drill Bank ✅ · Photo V-Carve ✅ · Quick Engrave ✅ · Sketch Carve ✅
· Rough 3D ✅ · Finish 3D ✅ · Texture ✅ · Drag Knife ✅ · Prism ✅ · Fluting ✅
· Chamfer ✅ · Bevel ✅ · Sweep/Moulding/Weave ✅ · Rotary wrap ✅ · Wrapped fluting ✅
· Laser ✅ (fill/picture/tab-ramp) · Extra beyond Aspire: Merged toolpath, Array copy, Nesting.

## Importers

| Format | Status |
|---|---|
| SVG / DXF / EPS / PDF / AI / DWG | ✅ via UnifiedImportRouter |
| STL / OBJ / 3MF / Heightmap / grayscale | ✅ |
| V3M 3D clipart | ⛔ honest stub — no public spec |
| SKP (SketchUp) | ⛔ honest stub — SDK-blocked |
| 3DM (Rhino) | ⛔ honest stub — spec-pending |

## Job sheet & export

| Aspire | VectorPilot | Status |
|---|---|---|
| Job sheet print (HTML template) | JobSheetHTMLTemplateEngine (bundled A4 template) + PDF renderer | ✅ |
| Post-processor selection on save | Post-template picker in Output panel (Export .tap / w/ template) | ✅ |
| Tool database (.vtdb) | ToolDatabase (10 types, cut-data table) | ✅ |
| Material presets (.mppa) | MaterialDatabase (defaults + CRUD dialog) | ✅ |

## Machine control

Simulator (virtual GRBL) E2E: connect → stream → ok-wait → hold (`!`) → resume
(`~`) → complete ✅ · jog `$J` ✅ · overrides M220/M221 ✅ · preflight rules
R013/R014/R017/keep-out + V-Carve open-path gate + checklist (spindle/work-zero) ✅
· real-hardware COM loop ⛔ not verifiable on this machine (no physical controller).

## Known honest gaps

1. Post-processor catalog: 3 shipped vs Aspire's 53+ (template engine supports
   arbitrary posts — catalog population is data work).
2. Gadget host (Lua) — only the keyhole gadget engine exists.
3. Cabinetry import lacks fixture-based validation.
4. 3D preview lacks camera animation (static orbit).
5. SketchUp/V3M/3DM importers — vendor-blocked stubs.
6. Real-hardware machine control unverified (simulator is the max coverage here).
