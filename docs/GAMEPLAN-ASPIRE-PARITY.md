# Game Plan — Aspire Parity (Vectric Aspire 12.5 feature surface)

Status legend: `[ ]` pending · `[~]` in progress · `[x]` done (tests green, committed)
Every item's DoD: port from Mac Swift authority (if one exists) or implement cleanly → xUnit tests → `dotnet build` 0 errors → full suite green → commit + push.

## Phase 1 — 3D modeling core (engine)
1. `[x]` **Component system + combine modes** — Component class (heightfield + transform + combine mode), Add/Subtract/Intersect/Highest compositing. Sources: `ShopPilotCore/Component.swift`, `CombineModes.swift`, `ComponentModifierEngine.swift`, `ComponentOperationEngine.swift`.
2. `[ ]` **Sculpt engine** — raise/lower/smooth/flatten brushes over a heightfield. Source: `SculptEngine.swift`.
3. `[ ]` **2-rail sweep / swept-profile relief** — sweep cross-sections along rails into a heightfield. Source: `SweepReliefEngine.swift`.
4. `[ ]` **Moulding toolpath (3D)** — swept profile along a rail as a toolpath strategy. Source: `SpecialtyToolpaths.swift` Moulding + `SweepReliefEngine.swift`.
5. `[ ]` **Grayscale bitmap ↔ heightfield** — export relief as grayscale PNG/BMP + import grayscale → relief (reuses BitmapTracer). Model menu items "Export as Grayscale Bitmap" / import.
6. `[ ]` **Modeling resolution / remesh** — resample a heightfield to a new cell size (Standard → 1M points equivalent).
7. `[ ]` **Sketch carving as 3D strategy** — depth-ramped carving along traced contours (upgrade SketchCarve port).
8. `[ ]` **V3M 3D clipart** — minimal V3M reader/writer for clipart import/export (stub with honest status if format is opaque).

## Phase 2 — toolpath breadth
9. `[ ]` **Tabs + ramps + leads generation** — wire SPK-1136a params into Profile/Pocket: 5 ramp types, tabs (2D/3D), lead-in/out shapes.
10. `[ ]` **Tiling** — split large jobs into tiles with overlap.
11. `[ ]` **Toolpath templates** — save/reuse strategy settings. Source: `ToolpathTemplates.swift`.
12. `[ ]` **Laser strategies** — Laser Cut / Laser Fill / Laser Picture (our own implementations; Aspire sells as add-on).
13. `[ ]` **Weave toolpath** — weave strategy if present in Mac (`SpecialtyToolpaths.swift`).

## Phase 3 — vector & 2D tools
14. `[ ]` **Vector validator** — open-vector / self-intersection detection.
15. `[ ]` **Fillet / extend / trim** — Source: `ShopPilotGeometry/FilletExtend.swift`.
16. `[ ]` **Node editing model** — add/delete/move nodes, convert segment to curve (engine model + tests; UI later).
17. `[ ]` **Text on curve** — place text along a path (transform glyph outlines).
18. `[ ]` **Draw tools** — arc, polygon, star, spiral, ellipse generators as VectorShape factories.
19. `[ ]` **Vector texture** — fill region with a repeat pattern.

## Phase 4 — import/export & data
20. `[ ]` **Cabinetry / part-list import** — 6 vendor mappings (Mozaik, KCD, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP) + PartListMapping.schema.json (generic mapping engine).
21. `[ ]` **`.crv3d`-style template system** — "New from template" package save/load (JSON).
22. `[ ]` **SKP import** — evaluate; heavy (SketchUp API) — implement or stub with honest status.
23. `[ ]` **3DM import** — evaluate; heavy (OpenNURBS) — implement or stub with honest status.

## Phase 5 — UI parity
24. `[ ]` **Strategy forms** — one form per ported engine wired into CutPanel (field parity per Aspire form capture).
25. `[ ]` **Docked Job Setup panel** — match Aspire's docked panel (size/material/datum/resolution).
26. `[ ]` **Material Settings dialog** — material DB CRUD (feeds/speeds) using ToolDatabase.
27. `[ ]` **Post-processor management UI** — post catalog (JSON), "Latest (V2)" versioning, install/update.
28. `[ ]` **Import hub** — one UI for all importers.
29. `[ ]` **3D preview playback** — combined view modes (wireframe/heightfield/combined), 2x–16x sim playback, shading.
30. `[ ]` **Command palette + preferences + shortcut map**.

## Phase 6 — packaging
31. `[ ]` **Inno Setup installer** + driver notes + README parity matrix.

Execution rule: work top-to-bottom; each item lands tested and committed before the next starts. The 2-hourly cron continues this list when no session is active.
