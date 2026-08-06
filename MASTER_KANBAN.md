# VectorPilot — Master Kanban

> Plan: [`PLAN.md`](./PLAN.md) · Cards: `VP-####` · Claim `[ ]` → `[~]` + worklog · DoD = ported harness green (identical numbers), not build.

## M0 — Foundation & handoff

- [ ] **VP-1000** Spec pack — emit from Mac repo into `docs/spec/`: document JSON schema (from Codable models), 72 preset JSON, tool DB seed JSON (13 classes/17 defaults), golden G-code files, verify PASS-line manifest
  - AC: `docs/spec/` contains schema.md + 4 JSON/asset sets; goldens byte-identical to Mac `fixtures/`
- [ ] **VP-1001** Freeze Mac baseline commit (`4b5311d`) as port reference
- [ ] **VP-1002** Scaffold solution (Geometry/Engine/Serial/App/Tests + assets + docs), `.gitignore`, CI (windows-latest: restore/build/test), initial board
  - AC: fresh clone → `dotnet build` + one ported smoke test green on the PC
- [ ] **VP-1003** Windows live capture (reference trial PC) → merge `docs/LIVE_CAPTURE.md`
  - AC: capture report covers menus, job setup, 2D/3D, toolpath forms, machine, output; trial limitations listed
- [ ] **VP-1004** PC environment per `docs/PC_SETUP.md` (toolchain, drivers, installer exe)

## M1 — Geometry + document model

- [ ] **VP-1100** Vector kernel translation (primitives, transforms, offsets, booleans, node edit, alignment); CoreText → DirectWrite adapter
- [ ] **VP-1101** DXF/SVG import + DXF/SVG export
- [ ] **VP-1102** Document model + `.shoppilot` JSON round-trip, schema frozen (mirror Swift Codable keys)
- [ ] **VP-1103** Harness: geometry verifies (0210/0211/0214/0215/0500/1101*/1120/1125/1137) + import-torture 28 checks

## M2 — Toolpaths P0 (Profile / Pocket / V-Carve / Drill)

- [ ] **VP-1200** Param models per reference §R2 key set + defaults (FM-06 flat depth, FM-15 allowance)
- [ ] **VP-1201** Engines + recalc/dirty + toolpath tree
- [ ] **VP-1202** Harness: 1133*/1136a–d/ProfileToolpath/VCarveClear/0600/0601/0603/0604/0319/0415/0417a/0418 + Golden25D/3DGolden

## M3 — Machine control

- [ ] **VP-1300** IMachineTransport + simulator; streamer, status parser, hold/resume/reset, TX/RX console
- [ ] **VP-1301** System.IO.Ports transport + COM enumeration
- [ ] **VP-1302** Safety chrome (e-stop, no auto-start, disconnect alarm)
- [ ] **VP-1303** Harness: 0404a/0404c/1104/1104a + FMR013/014/016/019 + GRBL goldens

## M4 — UI (WPF)

- [ ] **VP-1400** Stage-rail shell + command palette (UX_STAGE_SYSTEM.md decisions, Windows idioms)
- [ ] **VP-1401** Job setup (72 presets), design canvas, toolpath tree + inspector, machine panel
- [ ] **VP-1402** Template post engine (GRBL in/mm + generic) + HTML job sheet → PDF

## M5 — 3D preview (DX11)

- [ ] **VP-1500** Heightfield renderer + simulation + playback (2x–16x) + waste removal
- [ ] **VP-1501** STL→heightfield, 3D rough/finish, compositing + sculpt (verified engines)

## M6 — Data, parity, packaging

- [ ] **VP-1600** Data assets (presets, tool DB 3-part linkage, materials, job sheet)
- [ ] **VP-1601** Import STL/EPS/PDF/bitmaps → trace; export DXF/SVG/STL/PDF
- [ ] **VP-1602** Specialty strategies (rotary, inlay, texture, sketch-carve, photo-v-carve, drag-knife, quick engrave)
- [ ] **VP-1603** Inno Setup packaging, signing, auto-update, driver notes, 4K/DPI

## Work log

### 2026-08-06 — plan published (Hermes agent)
- PLAN.md, AGENTS.md, MASTER_KANBAN.md, README.md, docs/ (PORT_MANIFEST.md, PC_SETUP.md) written; repo created private on GitHub (`0xzgbot/VectorPilot`).
- Next claim: VP-1000 (spec pack) — quick win, unblocks everything.
