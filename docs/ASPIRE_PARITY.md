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
| Gadget system (Lua + HTML) | **Lua gadget host** (MoonSharp, hard sandbox + timeout) with a script editor, 4 shipped examples and an add-to-sheet path, plus the Keyhole and Corner Rounding gadgets | ✅ Lua; HTML dialogs not implemented |
| Cabinetry import (CSV PartListMapping, 5 transformation types) | CabinetryImport.cs (Mozaik/KCD/CabinetSense/CabinetPartsPro/Polyboard/SmartWOP), **all six validated against real vendor fixture files** under tests/fixtures/cabinetry | ✅ |
| 3D preview (OSG camera/shaded/AA) | WPF Viewport3D: heightfield mesh, toolpath overlay, ghost diff, playback transport, **animated camera** (continuous orbit + eased Iso/Top/Front/Right viewpoints, distance-preserving) | ✅ |
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
2. ~~Gadget host (Lua)~~ — CLOSED: MoonSharp host, sandbox verified (io/os/require all
   blocked, infinite loop killed). Aspire also allows HTML gadget dialogs; VectorPilot
   exposes a script editor instead.
3. ~~Cabinetry import lacks fixture-based validation~~ — CLOSED: six vendor fixtures
   (comma + tab), 39 tests covering each vendor's own header vocabulary.
4. ~~3D preview lacks camera animation~~ — CLOSED: continuous orbit + eased named
   viewpoints, wired to the Model stage and verified live (clicking Orbit flips the
   button to Stop). Shaded/AA parity with OSG is still not claimed.
5. SketchUp/V3M/3DM importers — vendor-blocked stubs.
6. Real-hardware machine control unverified (simulator is the max coverage here).
7. ~~Thread milling is not a registered strategy~~ — CLOSED: `threadmill` key
   registered and selectable; helical interpolation with per-pass radial stepover.
8. Pocket clearing is contour-offset loops + a clipped raster, not Aspire's full
   offset pocket. The raster no longer overhangs a curved wall (it clips against the
   inset boundary in both axes, not just X), and no cut move leaves a circular pocket.
   But it is still a hybrid: the raster re-covers ground the loops already cleared
   rather than filling only the leftover region. Suppressing it by predicting
   "the loops got everything" was tried and reverted — it left a small rectangle's
   floor uncut, and a redundant pass is safer than a missed one.
9. V-carve now cuts a medial axis: a discrete clearance field on a grid, ridge cells
   chained into polylines, depth from local clearance. A dumbbell's bulbs cut deeper
   than its neck and the interior is genuinely visited (outline-only carving never
   reached it). Still NOT Vectric-equivalent: the skeleton is a grid approximation
   rather than an exact medial axis, and there is no separate flat-area clearing pass
   for regions wider than the bit can reach in one plunge.
10. A7 "real UI automation" is UIAutomation via PowerShell + pyautogui
    (`tools/ui_verify.py`), **not** FlaUI as the card specified. It does drive the
    live app and read real control state, but the card's stated dependency was never
    added.
11. Engines that shipped with no app call-site are now reachable: nesting (Design),
    tiling (Output, one program per tile), array copy (Cut), fillet/extend (Design),
    the vector validator (Design + a Cut guard that refuses area strategies on an
    all-open selection), toolpath templates (Cut), and three previously unselectable
    strategies — Rotary Wrap, Wrapped Fluting, Drill Bank. 27 strategies in the combo.
12. Still genuinely missing vs Aspire: HTML gadget dialogs (only a Lua script editor),
    shaded/anti-aliased OSG-quality preview, and a true offset-pocket / exact
    medial-axis pair as noted in 8 and 9.
