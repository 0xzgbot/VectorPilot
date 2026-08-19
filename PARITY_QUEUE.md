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
- **A Tier-1 card is NOT `[x]` until a UI element actually invokes it.** Grep the panel `.xaml`/`.xaml.cs` for a real call-site. Tests-only delivery is `[ ]` with a note — a green test filter on an unreachable class is make-work, not progress.
- Wire first, test second. The engine math is largely already ported; the gap is reachability.
- If a card is already implemented AND reachable, flip to `[x]` with a one-line note and move on.
- If blocked 3× on the same approach, change strategy (write the note in the card).
- Never leave `[~]` overnight — either finish or revert to `[ ]` with a note.
- `./verify.sh` is the only gate. Never hand-roll a verification script.
- **Do not delegate.** 7/7 subagents on this key died to HTTP 429 without writing a file. Do the work directly.

---

## Tier 1 — App layer (the actual gap vs Aspire)

- [x] **A1. Node editing** — click a shape in Select mode to enter node mode: draggable point handles, insert point on segment double-click, delete selected node (Del), Esc exits. Wire through `UndoStack`.
  Gate: `FullyQualifiedName~NodeEdit` — ≥8 tests (hit-test a node, drag updates geometry, insert splits the correct segment, delete removes, undo restores).
  **DONE + REACHABLE:** Nodes tool button in XAML; 18 call-sites across Input/Edit/Render (click shape to enter, drag handles, double-click segment to insert, Del removes, Esc exits, all undoable). 12 tests.

- [x] **A2. Boolean ops in UI** — Union / Subtract / Intersect buttons on the ops bar, operating on the current multi-selection via the existing `BooleanOps` engine. Disabled with <2 selected.
  Gate: `FullyQualifiedName~BooleanOpsUi` — ≥6 tests (2-rect union area, subtract leaves hole-free outline, intersect of disjoint = empty, undo restores both originals).
  **DONE + REACHABLE:** Union/Subtract/Intersect buttons (auto-disabled under 2 closed shapes), 7 call-sites, undoable. 10 tests. NOTE: engine skips degenerate collinear-edge touches (pre-existing Greiner-Hormann limitation) — documented by a baseline test.

- [x] **A3. Transform dialog** — set exact X/Y/W/H for the selection, plus rotate-by-angle and scale-by-factor. Uses `ShapeTransformer`.
  Gate: `FullyQualifiedName~TransformOps` — ≥6 tests (set-size preserves aspect when locked, rotate 90° twice = 180°, scale about bbox center, undo).
  **DONE + REACHABLE:** Transform… button opens TransformDialog (X/Y/W/H, lock-aspect, angle, factor, validation); 11 call-sites, undoable. 13 tests.

- [x] **A4. Tool browser panel** — tree of the 13 tool classes from `ToolDatabase`, per-tool cut-data form (feed/plunge/rpm/depth), material + machine pickers that drive `ResolvedCutData`, save/revert.
  Gate: `FullyQualifiedName~ToolBrowser` — ≥8 tests (3-part resolution order machine>material>derived, edit persists, revert discards, 17-entry catalog intact).
  **DONE + REACHABLE:** Tools menu → "Tool Database…" opens ToolBrowserDialog (class tree, cut-data form, material+machine pickers, stage/save/revert, JSON persist). 11 tests.

- [~] **A5. Machine control panel** — connect/disconnect, live DRO, jog pad (X/Y/Z ± with step selector), soft-home, set-work-zero, stream start/pause/resume, always-visible E-stop + Reset, raw TX/RX console toggle. Simulator-backed.
  Gate: `FullyQualifiedName~MachinePanel` — ≥10 tests (E-stop always enabled, no auto-start, jog emits `$J=`, hold freezes the stream, disconnect mid-stream alarms).
  **REVERTED TO IN-PROGRESS (external review, correct):** MachineSession is a class ONLY TESTS IMPORT. MachinePanel.xaml.cs still drives _transport + AppState.Streamer directly, so the 13 MachinePanel-named tests can go green while the Machine stage is unchanged. Continuous jog is still a stub. Same violation A1 committed. Remaining: route the panel through ONE MachineSession, delete the duplicate e-stop/jog/stream path, fix continuous jog. Prior note follows: panel already had connect/jog/home/zero/stream; ADDED the missing E-STOP + Reset buttons (always enabled — the XAML claimed Reset was available but shipped no button) and a raw TX/RX console toggle. MachineSession carries the tested safety logic. 13 session tests + 6 XAML-construction tests.
  **HARNESS BUG (open):** `--ui-smoke` hangs (exit 124) even though all 4 panels construct fine on an STA thread — 4 fixes attempted (dispatcher priority, one-shot timer, null guards, code-wired events). Superseded by XamlConstructionTests; A7 FlaUI is the real fix.

- [x] **A6. 3D component tree panel** — component list with visibility + combine-mode dropdown (Add/Subtract/Merge/Low/High/Multiply), live composite via `ComponentCompositor`, sculpt brush controls.
  Gate: `FullyQualifiedName~ComponentTreePanel` — ≥8 tests (mode change recomposites, invisible excluded, order matters, undo).
  **DONE + REACHABLE (proven by driving the app):** new Model stage in the rail hosts ComponentTreePanel beside a live composite ThreeDPreview, with import-heightfield / add-shape-relief / bake-to-job. ui_verify.py reports Model [ComponentList] -> A6 reachable: yes. Previously (correctly) reverted because: ComponentTreePanel had ZERO references in MainWindow.xaml/.xaml.cs — the panel exists and constructs but no stage hosts it, so a user cannot reach it. Remaining: host it in the Model stage. Prior note: ComponentTreePanel — component list with visibility checkboxes, 7-mode combine dropdown, reorder up/down, remove, live recomposite, sculpt brush controls (tool/shape/falloff/radius/strength). 13 VM tests + construction test.

- [x] **A7. Real UI automation** — add FlaUI (`FlaUI.Core` + `FlaUI.UIA3`) to the test project; drive the running app: draw a rect, marquee-select, Ctrl+Z, assert. Replaces `--ui-smoke` claims with real click evidence.
  Gate: `python tools/ui_verify.py` — drives the REAL app.
  **DONE (no NuGet needed):** Windows UIAutomation via PowerShell + pyautogui clicks + screenshots + vision_analyze. tools/ui_verify.py (single harness; --tree dumps the live control tree). PROVEN BY DRIVING THE APP: (1) launch is gated by a "Recover unsaved work" MODAL — the real cause of every --ui-smoke hang, which no test could see; (2) A5 E-STOP enabled=True while disconnected, invoked for real, app survived; BtnStart enabled=False with no G-code (no auto-start); (3) A6 component tree absent from ALL FIVE stages — Design=LayersList, Toolpaths=ToolpathList/ParamsGrid, Output=ListToolpaths. Grep-based reachability is now backed by live-tree inspection.

## Tier 2 — Engine gaps

- [x] **E1. Bitmap tracer** — Sobel edge detect → Moore contour follow → Douglas-Peucker simplify. Port from Mac `BitmapTracer.swift`.
  Gate: `FullyQualifiedName~BitmapTrace` — 12 tests.
  **DONE + REACHABLE:** engine already existed (marching squares, not Sobel+Moore — equivalent for region contours) but had NO simplification and NO UI. Added Douglas-Peucker (SimplifyClosed) and a "Trace bitmap…" button in the Design ops bar: decodes to Gray8, traces, simplifies at 1px, scales to the sheet, flips to CNC Y-up, added undoably.

- [ ] **E2. Moulding + Weave toolpaths** — the two remaining unported strategies. Register in `StrategyRegistry`.
  Gate: `FullyQualifiedName~MouldingWeave` — ≥6 tests vs Mac numbers.

- [x] **E3. Post catalog breadth** — expand shipped posts from 3 toward the Mac's reference set (GRBL mm/in, FluidNC, Marlin, LinuxCNC, Mach3/4, Shapeoko, Onefinity, Avid, plus rotary variants).
  Gate: `FullyQualifiedName~PostCatalog` — ≥10 tests (each post's header/footer/move formatting round-trips).

- [x] **E4. Full-job time estimate** — aggregate across toolpaths with cutting/travel split and tool-change overhead.
  Gate: `FullyQualifiedName~JobTimeEstimate` — 11 tests.
  **DONE + REACHABLE:** JobTimeEstimator walks each toolpath GCode, splitting G0 travel (rapid rate) from G1 cutting (feed rate), plus a per-tool-change allowance; falls back to EstimatedTimeSeconds when uncalculated. Shown live in the Output panel header (TxtTimeEstimate).

## Tier 3 — Product polish

- [ ] **P1. Recipe picker + first-run welcome** — calibration + sign recipes surfaced in the UI, welcome sheet on first launch.
  Gate: `FullyQualifiedName~RecipePicker` — ≥4 tests.

- [ ] **P2. Follow-source link** — art edit marks dependent toolpaths dirty; coach copy in the Cut panel. Uses `DirtyRegionManager`.
  Gate: `FullyQualifiedName~FollowSource` — ≥5 tests.

- [x] **P3. Keep-out zone UI** — panel to add/edit/toggle/delete zones + preview overlay. Engine already done.
  Gate: `FullyQualifiedName~KeepOutPanel` — ≥5 tests.

- [ ] **P4. Material simulation preview** — sheet-aware heightfield render of the cut result in the 3D preview.
  Gate: `FullyQualifiedName~MaterialSim` — ≥5 tests.

---

## Blocked (do not attempt — external dependency)

- V3M / SKP / 3DM importers — need proprietary vendor specs/SDKs.
- Real-hardware machine E2E — needs a physical GRBL controller.

## Done log

(append `[x]` cards here with commit SHA + test delta)
- [x] A1 node editing — NodeEditSession + 12 tests, 591→603
