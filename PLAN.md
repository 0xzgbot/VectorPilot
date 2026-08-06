# VectorPilot — Windows Conversion Plan

**App:** VectorPilot · **Repo:** `github.com/0xzgbot/VectorPilot` (private) · **Date:** 2026-08-06 · **Status:** Plan approved, M0 ready
**Sibling app:** ShopPilot (macOS, SwiftUI) — same product family, same document schema, independent codebase.
**Source studied:** ShopPilot repo (44,846 LOC Swift) · reference CAM installer re-unpacked (V12.5.1.0 Build 12738, 1,368 files, exe + SQLite + data payloads forensically examined) · planning docs (`INSTALLER_BREAKDOWN.md`, `FEATURE_PARITY_MATRIX.md` §R, `WINDOWS_EXPLORER_PROMPT.md`, `UX_STAGE_SYSTEM.md`).

---

## 1. Mission

Build **VectorPilot**: a native **Windows** CNC suite — design → toolpaths → preview → machine control — as the sibling of ShopPilot. Same product bar (professional-grade CAM feature surface, independently implemented), same safety rules, same document format; a **new codebase** in a new language on a new platform.

**What this is:** a *re-platform* — the engine logic is translated, not recompiled.
**What this is not:** a copy of any third-party app, format, or asset. Feature *names, defaults, and workflow order* are reference evidence only.

## 2. Definition of Done

1. **Installable Windows app** (Inno Setup, signed optional) running the full workflow: job setup → 2D design → toolpaths → 3D preview → post → machine control (GRBL/FluidNC).
2. **Harness green:** every ported verify reproduces the Mac engine's numbers — the ported xUnit suite + golden G-code files are the gate, not "builds."
3. **Document compat:** `.shoppilot` files round-trip between ShopPilot (Mac) and VectorPilot (Windows) with an identical JSON schema.
4. **Safety bar carried over verbatim:** e-stop always visible, no auto-start streaming, disconnect → stop + alarm, raw TX/RX console, software ≠ hardware e-stop.

## 3. Current state (what exists to port)

| Target (ShopPilot) | Files | LOC | Role | Port fate |
|---|---|---|---|---|
| `ShopPilotCore` | 111 | ~31k | Engine: toolpaths (Profile/Pocket/V-Carve/Drill/3D Rough/Finish/Rotary/Combine/Inlay/Texture/Sketch-Carve/Photo-V-Carve/Drag-Knife), G-code model, GRBL streamer + status parser, preflight, keep-out zones, tool DB, posts, job model, Metal preview layer | → `VectorPilot.Engine` (C#) — preview layer rewritten in DX11 |
| `ShopPilotGeometry` | 27 | ~9k | Vector kernel: offset, boolean ops, node edit, text→curves (CoreText), DXF/SVG import/export, bitmap trace (CoreGraphics/ImageIO), nesting | → `VectorPilot.Geometry` (C#) — text/trace get DirectWrite/Skia adapters |
| `ShopPilotSerial` | 4 | — | `MachineTransport` protocol: IOKit/ORSSerial real transport + simulator transport | → `VectorPilot.Serial` (C#) — System.IO.Ports impl + simulator |
| `ShopPilot` (UI) | 35 | ~4k | SwiftUI: stage rail, canvas, toolpath tree, machine controller, inspector forms | → `VectorPilot.App` (WPF) — UX decisions ported, pixels don't |
| Verify CLTs | **97** | — | Plain-Swift CLIs asserting engine behavior + hand-derived golden G-code | → `VectorPilot.Tests` (xUnit) — **the DoD** |
| XCTest | 429 | — | Unit tests (parser, streamer, geometry, toolpaths) | → port as xUnit alongside verifies |

**Portability split (import-based classification):** ~23.2k LOC Foundation-only (all engine math, G-code, parser, streamer, posts, tool DB, document model) · ~21.7k LOC platform-bound (Metal preview, CoreText, CoreGraphics trace, IOKit serial, SwiftUI UI + debug-only preview blocks — many are `#if canImport(SwiftUI) && DEBUG` around Foundation logic; classification is conservative).

Full file-by-file inventory with LOC + class: `docs/PORT_MANIFEST.md`.

## 4. Reference surface (the feature target — installer-verified)

- **Reference app stack:** native Win32 C++ x64 (MSVC140), OpenSceneGraph/OpenGL 3D, pstill PDF, BugSplat crash, NSIS installer. **No machine-control UI** — control = posts + machine DB. That gap is VectorPilot's differentiator.
- **Data formats (semantics only — never copied):** `postp.ppdb` SQLite (800 posts + 935 machine configs), `.vtdb` SQLite tool DBs with GUID 3-part linkage (`db_geom_id` / `db_cut_data_id` / `db_mach_cut_data_id` — confirms the tool DB design), 17 binary `.default` toolpath defaults, 72 stock sheets, 91 Lua gadgets.
- **`.pp` post grammar (pattern to mirror):** `VAR X_POSITION = [X|C|X|1.3]`, `UNITS`, `LINE_ENDING`, block numbering, `begin` blocks. GRBL family shipped: Grbl (inch/mm), Grbl WrapY2A (inch/mm), Easel-Grbl, OpenBuilds GRBL, Shapeoko.
- **Strategies (17 + variants):** Profile, Pocket, V-Carve, Drilling, Chamfer, Fluting, 3D Rough, 3D Finish, Swept Profile/Moulding, Texture, Quick Engrave, Bevel Carving, Thread Milling, Laser family, Photo V-Carve, V-Carve Inlay, Prism Carving, Plasma Profile.
- **Shared subsystems:** tabs (2D/3D/auto), ramps (5 types), leads (arc/line), ordering/sorting/merge, boundaries + offsets, tolerances, climb/conventional, keep-out zones, tiling, nesting, toolpath templates, 2x–16x simulation.
- **Job types:** single / double-sided / rotary. **2D:** full create/edit incl. node edit, boolean, offset, text-to-curves, bitmap trace, layers. **3D:** components + combine modes + sculpt, STL/3DM/SKP import.
- **Output:** 964 posts, HTML job sheet, machine DB. **Trial limits:** export disabled, laser gated (our paid/full tier map).

Full detail: `docs/PORT_MANIFEST.md` §5 + ShopPilot `docs/planning/INSTALLER_BREAKDOWN.md` + `FEATURE_PARITY_MATRIX.md` §R.

## 5. Stack decision

| Option | Engine | UI | 3D preview | Verdict |
|---|---|---|---|---|
| **A. C#/.NET 8 + WPF (chosen)** | C# class libs | WPF | DirectX 11 via HelixToolkit.SharpDX / Vortice | Best tooling, System.IO.Ports, System.Text.Json, MSIX/Inno packaging. Swift→C# near-mechanical (structs, enums→records, protocols→interfaces, async/await matches). |
| B. C#/.NET + Avalonia | same | Avalonia | no native 3D — embed Silk.NET/OpenGL | Only if cross-platform UI later. Costs the 3D viewport. |
| C. Swift engine DLL + C# UI | Swift on Windows | WPF | same as A | FFI across every call, Swift async can't cross cleanly, toolchain friction. Two languages, worst of both. |
| D. Tauri/Rust · Qt/C++ · Flutter | Rust/C++ | web/Qt | wgpu/OpenGL | All real; all slower to green. Rust translation slower than C#, Qt velocity lowest, Flutter desktop 3D immature. |

**Decision: A.** Engine = UI-independent class libraries so the harness tests the engine with zero UI and the UI framework stays swappable.

## 6. Target architecture (Windows)

```
VectorPilot.sln
├── VectorPilot.Geometry/   C# lib — vector kernel, DXF/SVG, text (DirectWrite), trace (Skia)   ← ports ShopPilotGeometry
├── VectorPilot.Engine/     C# lib — session, toolpaths, G-code model, streamer, parser,       ← ports ShopPilotCore
│                            preflight, keep-out zones, tool DB, posts, job model (no UI)
├── VectorPilot.Serial/     C# lib — MachineTransport protocol + System.IO.Ports impl + simulator
├── VectorPilot.App/        WPF — stage rail, design canvas, machine panel, DX11 preview host
├── VectorPilot.Tests/      xUnit — the ported verify CLTs + goldens + 429-test equivalents (the DoD)
├── assets/                 JSON seeds (72 presets, tool DB), golden G-code, job-sheet template
└── docs/                   this plan, LIVE_CAPTURE.md, parity matrix, spec pack
```

Key seams (mirror the Mac architecture):
- `IMachineTransport` protocol + `SimulatorTransport` → machine work needs zero hardware.
- `Job` model + JSON serializer → `.shoppilot` interop contract.
- Post engine = template grammar (`[X|C|X|1.3]`-style) + template files (GRBL in/mm, generic, rotary wrap).

## 7. Port strategy principles

1. **Two repos, one spec.** ShopPilot (Mac) stays the Swift flagship; VectorPilot is a new repo. Shared artifacts: document schema, golden G-code, verify assertions, data seeds, live-capture reference.
2. **The harness is the DoD.** Port order = verify order. A strategy is "ported" when its verifies pass as xUnit with **identical numbers**; goldens copied verbatim (platform-independent text).
3. **Documents must round-trip.** JSON schema frozen in M1 (mirror the Swift Codable keys exactly). Extension decision: keep `.shoppilot` for drop-in compat or adopt `.vectorpilot` with identical schema — decide at M1, schema is the contract either way.
4. **Simulator-first, always.** Transport protocol + simulator first; live GRBL last, on real hardware, with AGENTS.md §2 safety rules.
5. **Slices, not big-bang.** One strategy family per milestone: engine → data → UI → harness → green → commit.
6. **Safety non-negotiables carry over verbatim** (AGENTS.md §2 of the Mac repo).

## 8. Milestones

### M0 — Foundation & handoff  *[can start now]*
**Goal: any machine can bootstrap the port in one session.**
- [ ] **VP-1000** Emit the spec pack from the Mac repo into `docs/spec/`: document JSON schema (from Codable models), 72 preset JSON, tool DB seed JSON (13 classes/17 defaults), golden G-code files, verify PASS-line manifest.
- [ ] **VP-1001** Freeze Mac repo baseline commit (currently `4b5311d`) as the port reference.
- [ ] **VP-1002** Scaffold solution (layout above), `.gitignore` (bin/obj/.vs), CI workflow (windows-latest: restore/build/test), initial `MASTER_KANBAN.md` (this plan's cards).
- [ ] **VP-1003** Run the Windows live capture (`WINDOWS_EXPLORER_PROMPT.md`) on the reference trial PC → merge `docs/LIVE_CAPTURE.md` as the UI/UX acceptance reference.
- [ ] **VP-1004** PC environment per `docs/PC_SETUP.md` (git, .NET 8, VS2022/Rider, SQLite, 7-Zip, serial drivers, installer exe copy).
- **Exit gate:** fresh clone on the PC → `dotnet build` + first ported test green; capture report landed.

### M1 — Geometry + document model  *[~9k LOC]*
**Goal: the kernel and the file format.**
- [ ] **VP-1100** Vector kernel: primitives, transforms, offsets, boolean ops, node edit, alignment (translation from `ShopPilotGeometry`; CoreText → DirectWrite adapter).
- [ ] **VP-1101** DXF/SVG import + DXF/SVG export (pure parsers — direct translation).
- [ ] **VP-1102** Document model: Job/Sheet/Layer/ToolpathTree + `.shoppilot` JSON round-trip, **schema frozen**.
- [ ] **VP-1103** Harness: geometry verifies (0210/0211/0214/0215/0500/1101*/1120/1125/1137…) + import-torture fixtures (28 checks).
- **Exit gate:** geometry verifies green; a `.shoppilot` saved by the Mac app (fixtures) loads → identical JSON; import-torture 28/28.

### M2 — Toolpaths P0 (Profile / Pocket / V-Carve / Drill)  *[the money strategies]*
**Goal: form-field parity with the reference §R2 key set + correct defaults.**
- [ ] **VP-1200** Param models: Profile (7 pages: tabs/ramps/leads/corners/order), Pocket (offset/raster + clearance), V-Carve (engraving/flat-depth/overcut), Drill (peck/dwell/retract/helical). Defaults matched — the two that matter: V-carve flat depth (FM-06), 3D machining allowance (FM-15).
- [ ] **VP-1201** Engines + recalc/dirty model + toolpath tree (`markDirty()` semantics).
- [ ] **VP-1202** Harness: 1133*/1136a–d/ProfileToolpath/VCarveClear/0600/0601/0603/0604/0319/0415/0417a/0418 + goldens (Golden25D, 3DGolden).
- **Exit gate:** all P0 verifies + goldens green; form-field checklist test asserts every §R2 field present with the right default.

### M3 — Machine control  *[the differentiator]*
**Goal: GRBL/FluidNC control on Windows, simulator-first.**
- [ ] **VP-1300** `IMachineTransport` + simulator transport; streamer (ok-wait + char-count), status parser, hold/resume/reset, raw TX/RX console.
- [ ] **VP-1301** `System.IO.Ports` transport (COM enumeration + friendly names).
- [ ] **VP-1302** Safety chrome: e-stop/reset always visible, no auto-start, disconnect → alarm (AGENTS.md §2 ported).
- [ ] **VP-1303** Harness: 0404a/0404c/1104/1104a + FMR013/014/016/019 + GRBL dialect golden fixtures.
- **Exit gate:** simulator-driven stream verifies green; live loopback on the PC with a USB-serial adapter.

### M4 — UI (WPF shell)
**Goal: the stage-rail experience on Windows.**
- [ ] **VP-1400** Shell: stage rail (Setup → Design → Cut → Output per `UX_STAGE_SYSTEM.md`), command palette, progressive disclosure — UX decisions ported, Windows-native idioms.
- [ ] **VP-1401** Job setup (72 presets + material DB), design canvas (2D vector editing), toolpath tree + inspector forms, machine panel.
- [ ] **VP-1402** Save Toolpaths: template post engine (SPK-1134 grammar) shipping GRBL in/mm + generic; HTML job sheet → PDF (SPK-1135 pattern).
- **Exit gate:** full sign-path E2E (recipe → text → V-Carve → preview → post → machine panel) usable; screenshots vs `LIVE_CAPTURE.md`.

### M5 — 3D preview (DirectX 11)
**Goal: the visual trust anchor.**
- [ ] **VP-1500** Heightfield renderer + toolpath simulation + playback (2x–16x), machined-area/material colors, double-click waste removal — feature parity with the Metal preview spec.
- [ ] **VP-1501** 3D pipeline: STL→heightfield, 3D rough/finish engines, component compositing + sculpt brushes (verified engines from the Mac).
- **Exit gate:** preview of a 3D golden job matches the Mac app visually (side-by-side screenshots).

### M6 — Data, parity, packaging
**Goal: ship a Windows installer.**
- [ ] **VP-1600** Data assets: 72 presets, tool DB seed (3-part linkage), stock materials, job-sheet template.
- [ ] **VP-1601** Import breadth: STL, EPS/PDF (vector), PNG/JPG/BMP → trace; export DXF/SVG/STL/PDF.
- [ ] **VP-1602** Specialty strategies: rotary wrap, inlay, texture, sketch-carve, photo-v-carve, drag-knife, quick engrave (verified engine translations).
- [ ] **VP-1603** Packaging: Inno Setup, single-file publish, code signing (SmartScreen mitigation), auto-update, CH340/CP210x driver notes, 4K/DPI.
- **Exit gate:** clean install on a second PC; full sign job air-cut on a GRBL machine; `.shoppilot` shared with the Mac app both ways.

### M7 — (Optional) platform reunification
Avalonia or web UI later if the port becomes the future. Not in scope — the Mac app keeps shipping during the port.

## 9. Execution order & dependencies

```
M0 ─► M1 ─► M2 ─► M3 ─► M4 ─► M5 ─► M6
       └──► M3 can start as soon as M1's Job model + simulator land (∥ with M2)
       M4 needs M2 (forms) + M3 (machine panel) surfaces; M5 needs M2 engines
```
- Do **not** parallelize agents on the same wiring files (same lesson as the Mac repo: single compile lock, shared files → wedge). Sequential in-session slices per milestone; delegate only disjoint-file work.

## 10. Risk register

| Risk | Mitigation |
|---|---|
| Engine translation drifts from Mac semantics | Verifies + goldens are the gate — identical numbers, not "close." Mac repo stays buildable for cross-checks. |
| CoreText text→curves differs on Windows | DirectWrite adapter; golden text fixtures assert glyph-position parity; fallback = embedded outline engine. |
| 3D fidelity (Metal → DX11) | Simulator *math* is pure C# (only the renderer is new); side-by-side screenshot gate in M5. |
| Reference defaults drift (trial vs paid) | Live capture records trial-state defaults; §R annotated. Our defaults are our own — FM-06/FM-15 are the two must-match. |
| Solo-dev velocity | One milestone at a time, harness-gated, no stub-file landings (worklog must show green tests, not "builds"). |
| Serial driver chaos | Simulator-first removes hardware from the critical path; driver notes in `docs/PC_SETUP.md`. |

## 11. Agent operating rules (for Hermes/agent sessions on this repo)

1. Read `PLAN.md` + `AGENTS.md` + `MASTER_KANBAN.md` before claiming work. Claim `[ ]` → `[~]` on the kanban, append a worklog.
2. **DoD = harness green, not build green.** Engine changes require the ported verify(s) for that surface to pass with identical numbers; UI-only changes require `dotnet build` + the affected surface.
3. No stub-file landings: a card whose worklog says "builds cleanly" but has no passing test is **not done** — same rule as the Mac repo.
4. Anti-loop protocol: 3+ similar failures on one card → stop and change strategy (don't retry the same approach).
5. Commit + push per card. Never commit files a sibling agent left dirty.
6. When in doubt about engine semantics: read the **Mac source** (clone `ShopPilot` read-only) — it is the authority, verifies are the contract.

## 12. References & file map

| File | Content |
|---|---|
| `README.md` | Overview + status |
| `PLAN.md` | This document |
| `AGENTS.md` | Agent operating manual (short) |
| `MASTER_KANBAN.md` | Milestone cards (VP-####), claim/worklog board |
| `docs/PORT_MANIFEST.md` | 177-file inventory: LOC, portability class, 97 verify CLTs, data assets, reference surface |
| `docs/PC_SETUP.md` | PC environment + handoff checklist |
| `docs/spec/` | (M0) schema, seeds, goldens — the spec pack |
| `docs/LIVE_CAPTURE.md` | (M0) reference live-app capture from the trial PC |
| ShopPilot repo (`0xzgbot/ShopPilot`) | Mac flagship: engine semantics authority, planning docs (`INSTALLER_BREAKDOWN.md`, `FEATURE_PARITY_MATRIX.md` §R, `WINDOWS_EXPLORER_PROMPT.md`, `UX_STAGE_SYSTEM.md`) |
