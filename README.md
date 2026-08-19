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
| Component tree + combine modes | ⚠ engine + panel built, NOT hosted in any stage yet |
| Sculpt engine | ✅ |
| 2-rail sweep / extrude | ✅ |
| Weave | ❌ **not implemented** — volume/area estimate only, no toolpath |
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

## Status

- **671/671 tests green** (xUnit). Local gate is `./verify.sh` (Release + zero-warning);
  GitHub CI is weaker (Debug, no warning gate) — trust `verify.sh`.
  Release workflow (self-contained publish → tests → Inno Setup installer)
  green — the `vectorpilot-setup` exe is built per tag.

### Known engine shortcuts (shipping, not TODO)

These are locked in by goldens and are **not** Aspire-equivalent:

| Strategy | Actual behaviour |
|---|---|


| Weave | Volume/area estimate only — no mesh, no toolpath |

A test count is not a machine you can cut with. See `docs/vectorpilot-review.html`.
- **Done:** full Mac parity wave (SPK-0209 expressions + document variables,
  0216 unified import router, 0315 dirty-region resim, 0316 ghost diff,
  1134 post template engine v2 + rotary Y2A wrap, 1135 HTML job sheet → PDF,
  D13 fit curves, E22 model offset, H04 wrapped fluting), all dialog shells
  (materials, machine config, posts, command palette, preferences), layers
  panel, expression-enabled strategy param forms, sort/merge + dirty recalc,
  autosave + crash recovery, Ctrl+K palette, .tap/HTML/PDF exports, machine
  E2E loopback + ok-wait protocol tests, ShapeTransformer (flip/scale/rotate).
- **Honest stubs (spec/SDK-pending):** V3M, SKP, 3DM importers.
- **Not verifiable here:** machine control on physical hardware (simulator
  loopback is the maximum coverage; GRBL wiring notes below).

## Driver & Connection Notes

- **Simulator (default):** no driver needed — `SIMULATOR` port runs a virtual
  GRBL for testing streams and overrides.
- **GRBL boards (Arduino Uno/ Mega with GRBL 1.1):** install the CH340/CH341
  or FTDI VCP driver for your board's USB chip, then pick the COM port in
  Machine Configuration. Baud 115200. Enter the post-processor settings if
  your firmware variant needs it.
- **Machine wiring:** confirm the work envelope in Machine Configuration
  before the first run — the preflight check (R017 thickness drift) and
  keep-out zone rules run before every stream start.
- **E-stop / safety:** the transport handles `!` (hold), `~` (resume), and
  soft-reset; wire a physical E-stop to your controller's input per its
  manual — VectorPilot cannot override a hardware stop.
- **Units:** the serial layer streams in inches (G20); the engine layer works
  in mm — the Machine Config conversion handles the mapping.
- **File association:** the installer registers `.shoppilot` packages.
