# VectorPilot — Aspire & ShopPilot Parity Matrix

Windows C#/.NET 8 + WPF port of the macOS ShopPilot CNC app, benchmarked against
Vectric Aspire 12.5 (the reference feature surface). Status verified by the
harness: **355/355 tests green** (xUnit), golden G-code byte-parity, engine
smoke-test stub guard.

## Importers

| Format | Status | Notes |
|---|---|---|
| DXF (R12) | ✅ | ASCII, ENTITIES |
| SVG | ✅ | paths, transforms |
| EPS | ✅ | vector ops |
| PDF | ✅ | vector extraction |
| AI (PDF flavor) | ✅ | FlateDecode |
| DWG (R12) | ✅ | AC1009 binary |
| STL (ASCII+binary) | ✅ | → heightfield |
| OBJ | ✅ | → heightfield |
| 3MF | ✅ | ZIP+XML → heightfield |
| Bitmap trace | ✅ | marching squares → vectors |
| Grayscale → relief | ✅ | PNG/BMP |
| V3M clipart | ⏳ | status registry; spec-pending |
| SKP (SketchUp) | ⏳ | status registry; SDK-pending |
| 3DM (Rhino) | ⏳ | status registry; OpenNURBS-pending |
| Cabinetry part lists | ✅ | Mozaik, KCD, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP + JSON schema |

## Exporters

| Format | Status |
|---|---|
| DXF R12 | ✅ |
| STL (ascii/binary) | ✅ |
| OBJ | ✅ |
| EPS | ✅ |
| PDF (valid xref) | ✅ |
| Grayscale bitmap (BMP/PNG) | ✅ |

## Toolpath Strategies (20 in the registry)

Profile, Pocket, V-Carve, Drill (+DrillBank), Quick Engrave (2 flavors),
Prism, Fluting, Chamfer, Bevel Carving, Drag Knife, Texture, Inlay (pocket +
plug + recipes), Laser Cut/Fill/Picture, 3D Rough (z-level + rest), 3D Finish,
Photo V-Carve, Sketch Carving, Moulding — all ✅ with G-code output.
Shared subsystems: lead-in/out, tabs, ramps (smooth/zigzag/spiral), keep-out
zones, tiling, nesting, toolpath templates, sort/merge, array copy, rotary wrap.

## 3D Pipeline

| Feature | Status |
|---|---|
| Heightfield core + rasterizer | ✅ |
| STL/OBJ/3MF → heightfield | ✅ |
| Component tree + combine modes | ✅ |
| Sculpt engine | ✅ |
| 2-rail sweep / weave / extrude | ✅ |
| Modeling resolution (Standard 1M / High 4M) | ✅ |
| 3D rough + finish engines | ✅ |
| Toolpath simulator (material removal) | ✅ |
| WPF 3D preview + playback model | ✅ |
| Grayscale export of reliefs | ✅ |

## Document Model & Machine

- `.shoppilot` package save/load (manifest v0.2 + sheets + toolpaths) ✅
- Job templates (`.crv3d`-style "New from template") ✅
- Tool database (13 classes / 17 catalog / JSON) + 72 stock presets ✅
- Material settings DB ✅ · Post-processor catalog (Latest-V2 versioning) ✅
- GRBL + Universal post processors, job-sheet PDF ✅
- Machine control: transport, simulator, streamer, overrides (M220/M221),
  pause/resume, preflight rules (R013/R014/R017/keep-out) ✅
- Inno Setup installer + CI + release workflow ✅

## Remaining (cron)

Dialog shells for the UI services (material dialog, post manager window,
command palette window, job-setup panel polish), 3D preview view-mode polish,
README driver notes, and deeper verify-CLT parity ports (33 → goal 109).
