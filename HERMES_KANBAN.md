# VectorPilot — Hermes Kanban

For the **orchestrator** you prompt from Cursor. Workers (sub-agents) never own this file except to flip **their** card `[~]` → `[x]` after the critic passes.

Supersedes `MASTER_KANBAN.md` (stale M0–M6) and `PARITY_QUEUE.md` (single-agent, “do not delegate”). Those files stay as history.

**Repo:** `C:\Users\tmoph\OneDrive\Documents\cncresearch\VectorPilot`  
**Mac (read-only):** `../ShopPilot/Sources/**`  
**Product north star:** [beyond-Aspire plan](https://github.com/0xzgbot/VectorPilot) — LightBurn UX + gSender machine + Aspire-class 3D/photo. Not more combo-box strategies.

---

## Orchestrator protocol (you)

1. Read this file. Do **not** implement. Spawn workers.
2. Pick a **wave**. Only spawn the **Ready** cards whose `Parallel-OK` set is mutually exclusive (file lock).
3. For each spawn: copy the **Worker brief** at the bottom, fill `CARD-ID`, `OWN` paths, `FORBIDDEN` paths, `GATE`.
4. Worker must `git pull` then create branch `hermes/{card-id}` from `origin/main`. **Never commit to `main`.**
5. When a worker returns: run **Critic** on that branch (diff vs main). If fail, send the worker back. If pass, **you** merge to main (or open PR). Then flip the card `[x]` here in one commit on main.
6. If a worker hits HTTP 429 / empty diff: do not respawn 7 clones. Shrink the brief, retry **once**, then do that card yourself.
7. **Default concurrency is 1.** `dotnet build` is machine-wide (MSB3021). On 2026-08-20, 2 of 3 workers died at ~6–7 API calls with **empty diffs**. One finishing worker is faster wall-clock than three that write nothing. Fan-out only when a card is new-files-only, the brief is pre-verified, and you are not about to `verify.sh`.
8. **Never leave main dirty when spawning.** A worker's `git checkout -b` carries
   uncommitted orchestrator files onto its branch; deleting that branch then destroys
   them. Commit the board *before* dispatch. (Cost one recovery via `git reflog` +
   `cherry-pick` on 2026-08-20.)
9. **Critic must be briefed with the CURRENT main SHA.** I gave a critic `main@e50bda9`
   after main had advanced past it; it then flagged another card's merged files as
   forbidden on the branch under review. Two of three FAIL items were false positives.
10. **Do not `git checkout` while a worker holds the repo.** Workers share this one
   working tree, so an orchestrator commit can land on the worker's branch instead of
   main (happened with c5f1796). Verify `git branch --show-current` before committing,
   and `git rev-parse origin/main` after.
11. **429 reality on this key (2026-08-20):** Always `git diff --stat origin/main...HEAD` on `hermes/{id}` before believing a worker.
10. **OWN is a real path, not a class name.** Orchestrator greps before writing the brief. `HeightfieldFinishEngine` lives in `src/VectorPilot.Engine/Toolpaths/HeightfieldToolpath.cs` (same file as `HeightfieldFinishParams` and `HeightfieldRoughEngine`). A worker told to edit `HeightfieldFinishEngine.cs` will hunt or duplicate — and 429 kills exploration. Put **file + type + field + line** in the brief. Pattern that survives this key: **orchestrator recon, worker execute**.
11. If a listed OWN path is missing: worker **STOPS** and returns. Do not create a parallel file.

### Claim syntax in this file

`[ ]` ready · `[~]` a worker holds it (put branch name) · `[x]` merged to main · `[!]` blocked · `[-]` cancelled

Worker flips only its own line. Orchestrator owns wave headers and locks.

---

## File locks (why most cards cannot run together)

| Lock | Paths | Rule |
|------|--------|------|
| **APP-SHELL** | `src/VectorPilot.App/MainWindow.*`, `App.xaml*`, `AppState.cs` | **One** worker in the world |
| **CUT** | `src/VectorPilot.App/Controls/CutPanel.*`, `StrategyRegistry.cs`, `StrategyKeyMap.cs` | One worker |
| **DESIGN** | `src/VectorPilot.App/Controls/DesignPanel*` | One worker |
| **MODEL** | `src/VectorPilot.App/Controls/ModelPanel.*`, `ComponentTree*` , `ThreeDPreview.*` | One worker |
| **MACHINE** | `src/VectorPilot.App/Controls/MachinePanel.*`, `MachineSession.cs`, `src/VectorPilot.Serial/**` | One worker |
| **ENGINE-3D** | `src/VectorPilot.Engine/Toolpaths/HeightfieldToolpath.cs` (Finish **and** Rough engines), `SculptEngine.cs`, `SweepReliefEngine.cs`, compositor files | One worker — Finish vs Rough **same file** |
| **ENGINE-PHOTO** | `src/VectorPilot.Engine/Photo/LithophaneEngine.cs`, `src/VectorPilot.Engine/Toolpaths/PhotoVCarveEngine.cs`, `SketchCarveEngine.cs` | One worker |
| **ENGINE-GEO** | `src/VectorPilot.Geometry/**` | One worker |
| **TESTS** | `tests/VectorPilot.Tests/{CardFilter}*.cs` | Parallel OK if **new files only** |
| **DOCS** | `docs/ASPIRE_PARITY.md`, this kanban | Orchestrator only |

**Disjoint locks do NOT make two workers safe here.** `tests/VectorPilot.Tests.csproj`
line 18 has a `ProjectReference` to `VectorPilot.App`, so EVERY test run compiles the App.
An Engine worker whose files are untouched still cannot reach its test phase while an App
worker has `MainWindow.xaml.cs` half-edited — verified 2026-08-20: H-201's gate went red
three times on H-101's in-flight code, with errors mutating between runs, then passed
unchanged once H-101 compiled. Locks prevent *edit* collisions, not *build* collisions.
Prefer **one worker at a time**; if you must run two, expect the second's gate to be
unreliable until the first compiles.

Two workers may run iff their lock sets are **disjoint**. Tests that only **add** a new `*Tests.cs` do not take TESTS lock.

`dotnet build` / `verify.sh`: **one at a time**. Workers write code; orchestrator (or a dedicated Verify worker) runs the gate after merge, **or** workers verify on their branch after others have finished building. If MSB3021: kill `VectorPilot.exe`.

---

## Wave 0 — Locks and honesty (orchestrator, no spawn)

- [x] **H-000** Orchestrator: add `hermes/{id}` to `.gitignore` is **wrong** — do not. Confirm `git status` clean on main before Wave 1. Point `AGENTS.md` at this file (one sentence). No feature work.

---

## Wave 1 — Shell (serial except tests)

These take APP-SHELL / CUT / MACHINE. **Run one App worker at a time.** Engine workers from Wave 2 may start **only** if they touch **new Engine files** and no App.

### Ready (serial App)

- [x] **H-101** Beginner / Advanced + three job starters (Sign / Photo / 3D)  
  Locks: **APP-SHELL**, **CUT**  
  Parallel-OK: H-201, H-202 (engine-only)  
  OWN: `MainWindow.xaml(.cs)`, new `JobStarterOverlay.xaml`, `CutPanel` combo visibility  
  FORBIDDEN: Engine, MachinePanel, ThreeDPreview  
  Gate: `FullyQualifiedName~JobStarter` — Advanced shows full registry; Beginner shows ≤8 operations; Photo/3D starters exist as clickable UI  
  AC: User can start without seeing Thread Mill.

- [x] **H-102** Cuts/Layers list (LightBurn-style operations list)  
  Locks: **CUT**  
  Depends: H-101 merged  
  Parallel-OK: **none while H-202 is in flight** (MSB3021 + 429). After H-202 merge: new-file engine cards only.  
  OWN: `src/VectorPilot.App/Controls/CutPanel.xaml`, `CutPanel.xaml.cs`; optional **new** `src/VectorPilot.App/Controls/CutsListControl.xaml(.cs)`  
  FORBIDDEN: `MainWindow.*` except a one-line host if a named slot already exists; Engine  
  Gate: `FullyQualifiedName~CutsList`  
  AC: Each toolpath is a row (name, strategy, time); double-click opens **named fields** not a JSON blob; Calculate uses row ParamsJson.  
  Recon for brief: `CutPanel.xaml.cs` already has toolpath list + `CommitParamsForm` / `ParamsJson` — extend that list; do not invent a second registry.

- [x] **H-103** App-lifetime machine dock (gSender/LightBurn)  
  Locks: **APP-SHELL**, **MACHINE**  
  Depends: none if H-101 not touching MachinePanel — **conflict with H-101**. Do **after** H-101.  
  Parallel-OK: H-201–H-203  
  OWN: **new** `src/VectorPilot.App/Controls/MachineDock.xaml(.cs)`; `src/VectorPilot.App/MainWindow.xaml(.cs)` host strip; `src/VectorPilot.App/MachineSession.cs`; `src/VectorPilot.App/App.xaml.cs` if session must outlive the stage. Existing chrome: `Controls/MachinePanel.xaml(.cs)` — **move** E-stop/Hold/Connect, do not duplicate.  
  FORBIDDEN: CutPanel strategy math, Model 3D  
  Gate: `FullyQualifiedName~MachineDock`  
  AC: Leave Machine stage; Hold/E-stop still enabled. No auto-start.

- [x] **H-104** Frame job + click-to-jog on canvas  
  Locks: **DESIGN**, **MACHINE**  
  Depends: H-103  
  Parallel-OK: H-201–H-204  
  OWN: `src/VectorPilot.App/Controls/DesignPanel.Input.cs` (click-to-world), `DesignPanel.xaml(.cs)` if a Frame button is needed; `src/VectorPilot.App/MachineSession.cs` (Frame rapids). There is no `DesignPanel.cs` — partials are Edit/Input/Render.  
  FORBIDDEN: StrategyRegistry  
  Gate: `FullyQualifiedName~FrameJog`  
  AC: Frame emits rapids on simulator; click canvas jogs when connected (disabled if disconnected).

### Engine (can overlap Wave 1 App after H-101 is in flight **only** if new files)

- [x] **H-201** Lithophane heightfield (photo → thickness)  
  Locks: **ENGINE-PHOTO** (new files)  
  Parallel-OK: H-101–H-104, H-202, H-203  
  OWN: **new** `src/VectorPilot.Engine/Photo/LithophaneEngine.cs` + `tests/.../LithophaneEngineTests.cs`  
  FORBIDDEN: `VectorPilot.App/**`, existing PhotoVCarveEngine behavior unless calling it  
  Gate: `FullyQualifiedName~Lithophane`  
  AC: Light pixels → thicker (or documented invert); closed preview heightfield; no G-code required yet.

- [x] **H-202** Scallop-height 3D finish param  
  Locks: **ENGINE-3D**  
  Parallel-OK: H-201, H-101–H-103 (**not** H-204 if same finish files)  
  OWN: `src/VectorPilot.Engine/Toolpaths/HeightfieldToolpath.cs` only (types `HeightfieldFinishParams`, `HeightfieldFinishEngine`, **and** `HeightfieldRoughEngine` — no `HeightfieldFinishEngine.cs`). `HeightfieldData` ctor is `src/VectorPilot.Engine/Heightfield.cs`. StepOverMm is on Finish params; `Math.Max(0.1, …)` already floors stepover on rough.  
  FORBIDDEN: App  
  Gate: `FullyQualifiedName~ScallopFinish`  
  AC: Smaller scallop → denser G1; test two scallops not equal.

- [x] **H-203** DONE (RestRoughTests.cs only; engine untouched) — was tests-only: the rest algorithm ALREADY EXISTS (`PreviousToolDiameterMm` line 27, `IsRestRough` line 29, run-width skip lines 116-117 of `Toolpaths/HeightfieldToolpath.cs`). Verified `rough3d` defaults already serialize `previousToolDiameterMm:0` and the params grid renders every numeric key, so **NO App change is needed**. Do NOT write a new rest engine. Rest-rough leftover stock  
  Locks: **ENGINE-3D** — **conflicts H-202**. Queue **after** H-202.  
  Parallel-OK: H-201, App wave  
  OWN: `src/VectorPilot.Engine/Toolpaths/HeightfieldToolpath.cs` — type `HeightfieldRoughEngine` in that **same** file as H-202. Do not add `HeightfieldRoughEngine.cs`.  
  FORBIDDEN: App  
  Gate: `FullyQualifiedName~RestRough`  
  AC: Second tool only machines leftover vs first tool’s swept volume (or heightfield mask).

---

## Wave 2 — Photo product (App + photo engine)

- [x] **H-210** Photo workspace UI  
  Locks: **CUT** or new `PhotoPanel.xaml` hosted from MainWindow (**APP-SHELL** if new stage)  
  Depends: H-201, H-101  
  OWN: **new** `src/VectorPilot.App/Controls/PhotoPanel.xaml(.cs)` OR a region in `CutPanel` if no new stage; `src/VectorPilot.Engine/Photo/LithophaneEngine.cs` (read); `src/VectorPilot.Engine/Toolpaths/PhotoVCarveEngine.cs` (read). Prefer new PhotoPanel so CUT lock is only the host one-liner in `MainWindow.xaml`.  
  FORBIDDEN: Machine dock, Geometry kernel  
  Gate: `FullyQualifiedName~PhotoWorkspace` + grep XAML click handlers  
  AC: Preview updates before Calculate; empty image uses honest Empty() not fake `%`.

- [x] **H-211** Wire Photo V-Carve + lithophane + grayscale component through Cuts list  
  Locks: **CUT**, **ENGINE-PHOTO**  
  Depends: H-102, H-210, H-201  
  Parallel-OK: none with other CUT  
  Gate: `FullyQualifiedName~PhotoCnc`  
  AC: All three produce G1 through `StrategyRegistry.Compute` and appear as Cuts rows.

---

## Wave 3 — 3D product UI

- [x] **H-301** STL-to-stock wizard (MeshCAM-style)  
  Locks: **MODEL**  
  Parallel-OK: H-201 if not merged conflict; H-202 done  
  OWN: `src/VectorPilot.App/Controls/ModelPanel.xaml(.cs)`; STL import already in Engine — grep `StlImporter` (`src/VectorPilot.Engine/Import/` or similar). No `StlWizard.cs` unless you add **new** `Controls/StlImportDialog.xaml(.cs)`.  
  FORBIDDEN: CutPanel  
  Gate: `FullyQualifiedName~StlWizard`  
  AC: One STL → component on sheet bounds; cancel leaves job unchanged.

- [x] **H-302** Sculpt on 3D view (Aspire loop)  
  Locks: **MODEL**  
  Depends: H-301 not required  
  Parallel-OK: H-201, H-103 if locks disjoint — **MODEL vs MACHINE OK**  
  OWN: `src/VectorPilot.App/Controls/ThreeDPreview.xaml(.cs)`; `src/VectorPilot.Engine/SculptEngine.cs`. Component list: `Controls/ComponentTreePanel.xaml(.cs)`, `ComponentTreeViewModel.cs`.  
  FORBIDDEN: Serial  
  Gate: `FullyQualifiedName~SculptView`  
  AC: Drag on mesh changes heightfield; undo.

- [x] **H-303** Split 2D | 3D + component height/fade controls  
  Locks: **APP-SHELL**, **MODEL**, **DESIGN** — **serial, orchestrator-only tick**  
  Depends: H-101  
  Gate: `FullyQualifiedName~SplitView`  
  AC: Both views show; fade/scale height on selected component recomposites.

- [x] **H-304** Inverse mill (cavity from model) checkbox on 3D rough  
  Locks: **ENGINE-3D**, **CUT** (one param)  
  Depends: H-102  
  Gate: `FullyQualifiedName~InverseMill`  
  AC: Checkbox inverts Z vs stock; G-code max Z differs from normal.

---

## Wave 4 — Machine (gSender)

- [x] **H-401** Touch-plate probe wizard  
  Locks: **MACHINE**  
  Parallel-OK: H-201, H-202, H-301  
  OWN: `src/VectorPilot.App/Controls/MachineDock.xaml(.cs)` (after H-103) or `src/VectorPilot.App/Controls/MachinePanel.xaml(.cs)` if dock not merged; `src/VectorPilot.App/MachineSession.cs`; `src/VectorPilot.Serial/SimulatorTransport.cs` (emulate contact).  
  Gate: `FullyQualifiedName~ProbeWizard`  
  AC: Simulator can complete a probe; no motion if disconnected.

- [x] **H-402** Wasteboard surfacing wizard  
  Locks: **MACHINE**, **CUT** (generates a temp toolpath) — **after H-102**  
  Gate: `FullyQualifiedName~SurfacingWizard`  
  AC: Creates a raster facing program for sheet XY; user must press Start.

- [x] **H-403** Rotary mode (Y→A wrap at send time optional)  
  Locks: **MACHINE**, posts if needed  
  Parallel-OK: photo engine  
  Gate: `FullyQualifiedName~RotaryMode`  
  AC: Toggle documented; simulator accepts wrapped A or Y-as-A; no auto-start.

---

## Wave 5 — Easy power

- [x] **H-501** Material + bit preset fills Cut params  
  Locks: **CUT**  
  Gate: `FullyQualifiedName~MaterialBitPreset`  
  AC: Pick Hardwood + 6mm EM → feed/plunge/rpm match DB; Calculate uses them.

- [x] **H-502** Recipe: photo plaque / 3D coaster / sign  
  Locks: **APP-SHELL**  
  Depends: H-210, H-301  
  Gate: `FullyQualifiedName~FlashRecipes`  
  AC: Each recipe creates job + at least one toolpath ready to Calculate.

- [x] **H-503** Live sim playback on same 3D view while streaming  
  Locks: **MODEL**, **MACHINE** — serial  
  Gate: `FullyQualifiedName~LiveSim`  
  AC: Streamer line index moves a cursor on preview; E-stop stops both.

---

## Out of scope (do not spawn)

SKP / V3M / 3DM SDKs · FlaUI for its own sake · 5-axis Fusion · gadget HTML marketplace · extra posts “to reach N” · editing README as the card.

---

## Worker brief (paste into each sub-agent)

```
You are a Hermes WORKER, not the orchestrator.

CARD: {H-xxx title}
REPO: C:\Users\tmoph\OneDrive\Documents\cncresearch\VectorPilot
BRANCH: hermes/{H-xxx} from origin/main (pull first). Never push main. Never git add -A.

OWN files only (real paths from orchestrator recon — if a path 404s, STOP):
{paths}

Types/fields/lines from recon (do not glob-hunt):
{class + file + line + field}

FORBIDDEN (do not open to edit):
{paths}

DoD: UI click-site if the card is App (grep xaml). Tests call the same API the UI calls.
GATE: ./verify.sh "{filter}"
PATH: export PATH="$PATH:/c/Program Files/dotnet"
Kill VectorPilot.exe before build.

Do not write status reports. Do not start the next card. Do not edit HERMES_KANBAN.md except your [~]/[x] if orchestrator said so (prefer orchestrator flips [x] after merge).

Return to orchestrator: branch name, files touched, GATE output last line, anything FORBIDDEN you needed.
```

## Critic brief (paste)

```
You are Hermes CRITIC. Diff origin/main...HEAD on branch hermes/{id}.
Fail if: FORBIDDEN paths changed; no UI call-site on an App card; tests invent Title/Run on StrategyRegistry; card marked done without G1 where G-code required; commit on main.
Pass: short list of residual risks only.
```

## Parallel cheat sheet

| Same tick | Cards |
|-----------|--------|
| A | H-201 + H-101 (App vs new Photo engine file) |
| B | H-201 + H-202 — **NO** (both engine 3D/photo can collide on csproj — H-202 is Finish, H-201 is new folder: **YES** if H-201 only adds files and csproj is edited by **one** worker; orchestrator edits csproj **or** H-201 includes csproj and H-202 waits) |
| Safe default | **1 worker.** Fan-out only after recon + empty-diff check. |
| Never | Two of {CUT, APP-SHELL, MACHINE}; two cards on `HeightfieldToolpath.cs` |

**csproj rule:** only the worker whose OWN list includes `VectorPilot.Engine.csproj` / `VectorPilot.App.csproj` may add files. If the other needs a new `.cs`, they put it in a folder already in the glob, or wait.

---

## Done log

(orchestrator appends `H-xxx` + merge SHA)

- `H-000` — e946298 (clean main, AGENTS.md points here, README units claim corrected + 11 pinning tests)
- `H-201` — a7c3426 (LithophaneEngine + 11 tests; dark→thicker, Invert flag; new files only, no csproj change)
- `H-101` — 43e95c9 (Beginner/Advanced rail combo + Sign/Photo/3D starters; live combo 8 items, no Thread Mill; 26 tests)
- `H-101` critic fix — c5f1796 (rail combo no longer shows Beginner while state is Advanced; 3 tests drive the real click through the real window)
- `H-202` — 2b8e207 (ScallopHeightMm drives finish stepover, opt-in default 0; 13 tests)
- gate — 1ae10e3 (verify.sh rejects partial runs via a self-maintaining high-water floor)
- mythos wave — 356334e/c035cb5 on main: H-103 27b8529 (MachineDock pinned strip), H-104 16d9b46 (Frame + Ctrl+Click jog), H-203 04b3b0b (RestRoughTests only), H-210 42f3cea (Photo stage). 1449/1449 green after merging remote fixes 907b03b/a61b42b.
- `H-102` — b1bbcad (Cuts list is a ListView of real Toolpath items: Name/Strategy/Time/Dirty/Lines; selection survives refresh; **revived Array copy + Save/Apply template, which were dead because SelectedItem was always a string**; 11 tests)
- `H-211` — f223d94 (photo V-Carve + lithophane + grayscale land as real Cuts rows via StrategyRegistry.Compute)
- `H-301` — cd69dcc (STL-to-stock wizard: new StlImportDialog, opened from ModelPanel; one STL → component; cancel leaves job unchanged)
- `H-302` — 0e08114 (drag on the 3D mesh sculpts the selected component via ComponentTreeViewModel → SculptEngine + undo)
- merge(wave2/3) — 856f892 (H-211+H-301+H-302 to main in one branch hermes/h-211-301-302; FrameJogTests async conversion un-reds CI xUnit1031; 1464/1464 green)
- `H-303` — 2e31fde (split 2D|3D stage: ToggleSplitView + rail button + Ctrl+K command; component tree height/fade controls drive ComponentModifierEngine at composite time; CompositeChanged event; 5 tests)
- `H-304` — 478f74e (HeightfieldRoughParams.InverseMill flips the field about max → machines the mould cavity; inverseMill in DefaultsJson so the params form renders an editable bool row; 3 tests)
- `H-401` — 8ee6d6e (simulator G38.2 + ProbePlateZ; MachineSession.ProbeZAsync zeroes Z on plate top via G10 L20; ProbeWizardDialog from dock button; refuses when disconnected; 5 tests)
- `H-402` — 4e2b5ee (WasteboardSurfacing serpentine raster engine; SurfacingWizardDialog lands program as a real Cuts row, thread-safe shell refresh; dock button; nothing auto-streams; 4 tests)
- `H-403` — 57cd7a5 (MachineSession.SetRotaryMode/WrapYToA/SendWithRotaryWrapAsync — Y→A degrees wrap at send time; simulator tracks A wrapped [0,360); dock Rotary toggle with diameter prompt; never sends motion by itself; 4 tests + STAApplicationGate for suite-wide lazy Application creation)
- `H-501` — da82714 (CutPanel material+bit pickers resolve feed/depth/RPM through ResolvedCutData machine→material→derived; RPM rides ParamsJson into Calculate. Engine fix: derived fallback dropped the material argument; catalog names normalized MDF/Acrylic; 4 tests)
- `H-502` — d2be3bd (FlashRecipeManager photo plaque + 3D coaster; coaster ships a real pre-computed PocketEngine program on the job; RecipeDialog tiles; no text input needed; 3 tests)
- `H-503` — bd09781 (ThreeDPreview BeginLivePlayback/MoveLiveCursor/EndLivePlayback — machined green / pending gray / red head cross; MachinePanel's progress timer drives it via MainWindow.LiveSimPreview; E-stop stops both; 4 tests)
