# VectorPilot Parity Queue

Autonomous work queue. **One card per tick.** No summaries, no check-ins.

## Protocol (for any agent picking this up)

1. `cd ~/OneDrive/Documents/cncresearch/VectorPilot && git pull -q`
2. Find the **first** `[ ]` card below. Change it to `[~]`, commit that flip immediately.
3. Implement it. Mac source of truth: `../ShopPilot/Sources/**`. Match semantics exactly.
4. Write the tests named in the card's **Gate**.
5. `./verify.sh "<the card's filter>"` — must print `VERIFY PASS`.
6. Commit + push. Flip `[~]` → `[x]`. Push again.
7. Take the next card. **Do not stop between cards. Do not write a status report.**

Rules:
- If a card is already implemented, flip to `[x]` with a one-line note and move on.
- If blocked 3× on the same approach, change strategy (write the note in the card).
- Never leave `[~]` overnight — either finish or revert to `[ ]` with a note.
- `./verify.sh` is the only gate. Never hand-roll a verification script.

---

## Tier 1 — App layer (the actual gap vs Aspire)

- [~] **A1. Node editing** — click a shape in Select mode to enter node mode: draggable point handles, insert point on segment double-click, delete selected node (Del), Esc exits. Wire through `UndoStack`.
  Gate: `FullyQualifiedName~NodeEdit` — ≥8 tests (hit-test a node, drag updates geometry, insert splits the correct segment, delete removes, undo restores).

- [ ] **A2. Boolean ops in UI** — Union / Subtract / Intersect buttons on the ops bar, operating on the current multi-selection via the existing `BooleanOps` engine. Disabled with <2 selected.
  Gate: `FullyQualifiedName~BooleanOpsUi` — ≥6 tests (2-rect union area, subtract leaves hole-free outline, intersect of disjoint = empty, undo restores both originals).

- [ ] **A3. Transform dialog** — set exact X/Y/W/H for the selection, plus rotate-by-angle and scale-by-factor. Uses `ShapeTransformer`.
  Gate: `FullyQualifiedName~TransformOps` — ≥6 tests (set-size preserves aspect when locked, rotate 90° twice = 180°, scale about bbox center, undo).

- [ ] **A4. Tool browser panel** — tree of the 13 tool classes from `ToolDatabase`, per-tool cut-data form (feed/plunge/rpm/depth), material + machine pickers that drive `ResolvedCutData`, save/revert.
  Gate: `FullyQualifiedName~ToolBrowser` — ≥8 tests (3-part resolution order machine>material>derived, edit persists, revert discards, 17-entry catalog intact).

- [ ] **A5. Machine control panel** — connect/disconnect, live DRO, jog pad (X/Y/Z ± with step selector), soft-home, set-work-zero, stream start/pause/resume, always-visible E-stop + Reset, raw TX/RX console toggle. Simulator-backed.
  Gate: `FullyQualifiedName~MachinePanel` — ≥10 tests (E-stop always enabled, no auto-start, jog emits `$J=`, hold freezes the stream, disconnect mid-stream alarms).

- [ ] **A6. 3D component tree panel** — component list with visibility + combine-mode dropdown (Add/Subtract/Merge/Low/High/Multiply), live composite via `ComponentCompositor`, sculpt brush controls.
  Gate: `FullyQualifiedName~ComponentTreePanel` — ≥8 tests (mode change recomposites, invisible excluded, order matters, undo).

- [ ] **A7. Real UI automation** — add FlaUI (`FlaUI.Core` + `FlaUI.UIA3`) to the test project; drive the running app: draw a rect, marquee-select, Ctrl+Z, assert. Replaces `--ui-smoke` claims with real click evidence.
  Gate: `FullyQualifiedName~UiAutomation` — ≥5 tests, each driving actual mouse/keyboard input.

## Tier 2 — Engine gaps

- [ ] **E1. Bitmap tracer** — Sobel edge detect → Moore contour follow → Douglas-Peucker simplify. Port from Mac `BitmapTracer.swift`.
  Gate: `FullyQualifiedName~BitmapTracer` — ≥6 tests (traces a black square to 4 corners, tolerance reduces point count, noise rejected).

- [ ] **E2. Moulding + Weave toolpaths** — the two remaining unported strategies. Register in `StrategyRegistry`.
  Gate: `FullyQualifiedName~MouldingWeave` — ≥6 tests vs Mac numbers.

- [ ] **E3. Post catalog breadth** — expand shipped posts from 3 toward the Mac's reference set (GRBL mm/in, FluidNC, Marlin, LinuxCNC, Mach3/4, Shapeoko, Onefinity, Avid, plus rotary variants).
  Gate: `FullyQualifiedName~PostCatalog` — ≥10 tests (each post's header/footer/move formatting round-trips).

- [ ] **E4. Full-job time estimate** — aggregate across toolpaths with cutting/travel split and tool-change overhead.
  Gate: `FullyQualifiedName~JobTimeEstimate` — ≥5 tests.

## Tier 3 — Product polish

- [ ] **P1. Recipe picker + first-run welcome** — calibration + sign recipes surfaced in the UI, welcome sheet on first launch.
  Gate: `FullyQualifiedName~RecipePicker` — ≥4 tests.

- [ ] **P2. Follow-source link** — art edit marks dependent toolpaths dirty; coach copy in the Cut panel. Uses `DirtyRegionManager`.
  Gate: `FullyQualifiedName~FollowSource` — ≥5 tests.

- [ ] **P3. Keep-out zone UI** — panel to add/edit/toggle/delete zones + preview overlay. Engine already done.
  Gate: `FullyQualifiedName~KeepOutPanel` — ≥5 tests.

- [ ] **P4. Material simulation preview** — sheet-aware heightfield render of the cut result in the 3D preview.
  Gate: `FullyQualifiedName~MaterialSim` — ≥5 tests.

---

## Blocked (do not attempt — external dependency)

- V3M / SKP / 3DM importers — need proprietary vendor specs/SDKs.
- Real-hardware machine E2E — needs a physical GRBL controller.

## Done log

(append `[x]` cards here with commit SHA + test delta)
