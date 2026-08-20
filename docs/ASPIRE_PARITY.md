# VectorPilot ↔ Aspire V12.5 Parity Matrix

Source of truth: `ASPIRE_LIVE_CAPTURE.md` (live-capture ground truth, Aspire
Trial 12.5 installed at `C:\Program Files\Aspire Trial Edition 12.5`).
Generated 2026-08-10. Status key: ✅ parity, 🟡 partial, ❌ absent, ⛔ blocked.

## Feature surface (Aspire's 8 critical parity items)

| Aspire surface | VectorPilot | Status |
|---|---|---|
| 17 toolpath strategies | 20 in the strategy registry (Profile, Pocket, V-Carve, Drill, Drill Bank, Quick Engrave, Photo V-Carve, Sketch Carve, Rough 3D, Finish 3D, Texture, Drag Knife, Prism, Fluting, Chamfer, Bevel, Sweep, Moulding, Weave, Wrapped Fluting, Laser, Rotary Wrap) | ✅ superset |
| 7 tool types (End Mill, V-Bit, Ball Nose, Drill, Diamond Drag, Laser, Thread Mill) | 10 in ToolType (adds RadiusedEndMill, Engraving, RadiusedEngraving) | ✅ superset |
| 53+ post processors, 5 categories | **54 shipped templates** in 4 groups: industrial (Haas/Fanuc/SINUMERIK/Heidenhain/Okuma/Centroid), routers (GRBL/FluidNC/Mach3-4/WinCNC/Masso/UCCNC/PlanetCNC/ShopBot/X-Carve/LongMill/Shapeoko/Onefinity/Avid), firmware (Marlin/Smoothieware/Duet/LinuxCNC), laser+plasma; each mm+inch, plus rotary-Y2A. Template engine ([W\|M\|O\|F] grammar) still accepts arbitrary user posts; picker changes the exported `.tap` | ✅ 54/53+ |
| Gadget system (Lua + HTML) | Keyhole gadget engine (SPK-0907) + **Corner Rounding gadget** (real tangent arcs); no Lua/HTML gadget host | 🟡 |
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

1. ~~Post-processor catalog~~ — CLOSED: 54 shipped vs Aspire's 53+, verified live in
   the picker. Selection genuinely changes the exported `.tap`.
2. Gadget host (Lua) — only the keyhole gadget engine exists.
3. Cabinetry import lacks fixture-based validation.
4. 3D preview lacks camera animation (static orbit).
5. SketchUp/V3M/3DM importers — vendor-blocked stubs.
6. Real-hardware machine control unverified (simulator is the max coverage here).
7. Thread milling is not a registered strategy (no `threadmill` key in
   `StrategyRegistry`).
8. Pocket clearing is contour-offset loops + clipped raster, not Aspire's full
   offset pocket. Curved walls are followed; the interior remainder is still
   rastered.
9. V-carve depth comes from nearest-opposing-edge distance, not a true medial-axis
   skeleton. Width drives depth correctly, but it is not a Vectric-equivalent
   V-carve.
10. A7 "real UI automation" is UIAutomation via PowerShell + pyautogui
    (`tools/ui_verify.py`), **not** FlaUI as the card specified. It does drive the
    live app and read real control state, but the card's stated dependency was never
    added.
