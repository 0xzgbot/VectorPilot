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
7. Max **3 workers** at once on this PC (dotnet lock + OneDrive). Prefer **2**.
8. **Never leave main dirty when spawning.** A worker's `git checkout -b` carries
   uncommitted orchestrator files onto its branch; deleting that branch then destroys
   them. Commit the board *before* dispatch. (Cost one recovery via `git reflog` +
   `cherry-pick` on 2026-08-20.)
9. **429 reality on this key (2026-08-20):** a 2-worker fan-out died at 7 and 6 API
   calls with zero files written — same failure as the 7/7 wipeout in `AGENTS.md`
   rule 6. Both branches had **empty diffs**. Retry once with a shrunk brief, then
   do the card directly. Always `git diff --stat main hermes/{id}` before believing
   a worker.

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
| **ENGINE-3D** | `src/VectorPilot.Engine` heightfield / rough / finish / sculpt / compositor / sweep | One worker |
| **ENGINE-PHOTO** | `PhotoVCarveEngine`, `SketchCarveEngine`, grayscale / lithophane new files | One worker |
| **ENGINE-GEO** | `src/VectorPilot.Geometry/**` | One worker |
| **TESTS** | `tests/VectorPilot.Tests/{CardFilter}*.cs` | Parallel OK if **new files only** |
| **DOCS** | `docs/ASPIRE_PARITY.md`, this kanban | Orchestrator only |

Two workers may run iff their lock sets are **disjoint**. Tests that only **add** a new `*Tests.cs` do not take TESTS lock.

`dotnet build` / `verify.sh`: **one at a time**. Workers write code; orchestrator (or a dedicated Verify worker) runs the gate after merge, **or** workers verify on their branch after others have finished building. If MSB3021: kill `VectorPilot.exe`.

---

## Wave 0 — Locks and honesty (orchestrator, no spawn)

- [x] **H-000** Orchestrator: add `hermes/{id}` to `.gitignore` is **wrong** — do not. Confirm `git status` clean on main before Wave 1. Point `AGENTS.md` at this file (one sentence). No feature work.

---

## Wave 1 — Shell (serial except tests)

These take APP-SHELL / CUT / MACHINE. **Run one App worker at a time.** Engine workers from Wave 2 may start **only** if they touch **new Engine files** and no App.

### Ready (serial App)

- [ ] **H-101** Beginner / Advanced + three job starters (Sign / Photo / 3D)  
  Locks: **APP-SHELL**, **CUT**  
  Parallel-OK: H-201, H-202 (engine-only)  
  OWN: `MainWindow.xaml(.cs)`, new `JobStarterOverlay.xaml`, `CutPanel` combo visibility  
  FORBIDDEN: Engine, MachinePanel, ThreeDPreview  
  Gate: `FullyQualifiedName~JobStarter` — Advanced shows full registry; Beginner shows ≤8 operations; Photo/3D starters exist as clickable UI  
  AC: User can start without seeing Thread Mill.

- [ ] **H-102** Cuts/Layers list (LightBurn-style operations list)  
  Locks: **CUT**  
  Depends: H-101 merged  
  Parallel-OK: H-201, H-202, H-203  
  OWN: `CutPanel.*`, maybe new `CutsListControl.xaml`  
  FORBIDDEN: MainWindow chrome except hosting the control if already a slot  
  Gate: `FullyQualifiedName~CutsList`  
  AC: Each toolpath is a row (name, strategy, time); double-click opens **named fields** not a JSON blob; Calculate uses row ParamsJson.

- [ ] **H-103** App-lifetime machine dock (gSender/LightBurn)  
  Locks: **APP-SHELL**, **MACHINE**  
  Depends: none if H-101 not touching MachinePanel — **conflict with H-101**. Do **after** H-101.  
  Parallel-OK: H-201–H-203  
  OWN: new `MachineDock.xaml(.cs)`, `MainWindow` host strip, move E-stop/Hold/Connect onto dock; `MachineSession` lives on `App` not panel Unloaded  
  FORBIDDEN: CutPanel strategy math, Model 3D  
  Gate: `FullyQualifiedName~MachineDock`  
  AC: Leave Machine stage; Hold/E-stop still enabled. No auto-start.

- [ ] **H-104** Frame job + click-to-jog on canvas  
  Locks: **DESIGN**, **MACHINE**  
  Depends: H-103  
  Parallel-OK: H-201–H-204  
  OWN: `DesignPanel` click-to-world, `MachineSession` Frame (G0 rectangle of selection or sheet)  
  FORBIDDEN: StrategyRegistry  
  Gate: `FullyQualifiedName~FrameJog`  
  AC: Frame emits rapids on simulator; click canvas jogs when connected (disabled if disconnected).

### Engine (can overlap Wave 1 App after H-101 is in flight **only** if new files)

- [ ] **H-201** Lithophane heightfield (photo → thickness)  
  Locks: **ENGINE-PHOTO** (new files)  
  Parallel-OK: H-101–H-104, H-202, H-203  
  OWN: **new** `src/VectorPilot.Engine/Photo/LithophaneEngine.cs` + `tests/.../LithophaneEngineTests.cs`  
  FORBIDDEN: `VectorPilot.App/**`, existing PhotoVCarveEngine behavior unless calling it  
  Gate: `FullyQualifiedName~Lithophane`  
  AC: Light pixels → thicker (or documented invert); closed preview heightfield; no G-code required yet.

- [ ] **H-202** Scallop-height 3D finish param  
  Locks: **ENGINE-3D**  
  Parallel-OK: H-201, H-101–H-103 (**not** H-204 if same finish files)  
  OWN: `HeightfieldFinishEngine` / params — stepover from scallop + tool diameter  
  FORBIDDEN: App  
  Gate: `FullyQualifiedName~ScallopFinish`  
  AC: Smaller scallop → denser G1; test two scallops not equal.

- [ ] **H-203** Rest-rough leftover stock  
  Locks: **ENGINE-3D** — **conflicts H-202**. Queue **after** H-202.  
  Parallel-OK: H-201, App wave  
  OWN: `HeightfieldRoughEngine` rest pass  
  FORBIDDEN: App  
  Gate: `FullyQualifiedName~RestRough`  
  AC: Second tool only machines leftover vs first tool’s swept volume (or heightfield mask).

---

## Wave 2 — Photo product (App + photo engine)

- [ ] **H-210** Photo workspace UI  
  Locks: **CUT** or new `PhotoPanel.xaml` hosted from MainWindow (**APP-SHELL** if new stage)  
  Depends: H-201, H-101  
  OWN: new panel: import image, contrast/invert, three buttons Engrave / Lithophane / 3D-from-photo  
  FORBIDDEN: Machine dock, Geometry kernel  
  Gate: `FullyQualifiedName~PhotoWorkspace` + grep XAML click handlers  
  AC: Preview updates before Calculate; empty image uses honest Empty() not fake `%`.

- [ ] **H-211** Wire Photo V-Carve + lithophane + grayscale component through Cuts list  
  Locks: **CUT**, **ENGINE-PHOTO**  
  Depends: H-102, H-210, H-201  
  Parallel-OK: none with other CUT  
  Gate: `FullyQualifiedName~PhotoCnc`  
  AC: All three produce G1 through `StrategyRegistry.Compute` and appear as Cuts rows.

---

## Wave 3 — 3D product UI

- [ ] **H-301** STL-to-stock wizard (MeshCAM-style)  
  Locks: **MODEL**  
  Parallel-OK: H-201 if not merged conflict; H-202 done  
  OWN: `ModelPanel` import dialog: rotate, scale to thickness, bake component  
  FORBIDDEN: CutPanel  
  Gate: `FullyQualifiedName~StlWizard`  
  AC: One STL → component on sheet bounds; cancel leaves job unchanged.

- [ ] **H-302** Sculpt on 3D view (Aspire loop)  
  Locks: **MODEL**  
  Depends: H-301 not required  
  Parallel-OK: H-201, H-103 if locks disjoint — **MODEL vs MACHINE OK**  
  OWN: `ThreeDPreview` mouse sculpt → `SculptEngine`  
  FORBIDDEN: Serial  
  Gate: `FullyQualifiedName~SculptView`  
  AC: Drag on mesh changes heightfield; undo.

- [ ] **H-303** Split 2D | 3D + component height/fade controls  
  Locks: **APP-SHELL**, **MODEL**, **DESIGN** — **serial, orchestrator-only tick**  
  Depends: H-101  
  Gate: `FullyQualifiedName~SplitView`  
  AC: Both views show; fade/scale height on selected component recomposites.

- [ ] **H-304** Inverse mill (cavity from model) checkbox on 3D rough  
  Locks: **ENGINE-3D**, **CUT** (one param)  
  Depends: H-102  
  Gate: `FullyQualifiedName~InverseMill`  
  AC: Checkbox inverts Z vs stock; G-code max Z differs from normal.

---

## Wave 4 — Machine (gSender)

- [ ] **H-401** Touch-plate probe wizard  
  Locks: **MACHINE**  
  Parallel-OK: H-201, H-202, H-301  
  OWN: MachineDock + Serial probe sequence on simulator (emulate contact)  
  Gate: `FullyQualifiedName~ProbeWizard`  
  AC: Simulator can complete a probe; no motion if disconnected.

- [ ] **H-402** Wasteboard surfacing wizard  
  Locks: **MACHINE**, **CUT** (generates a temp toolpath) — **after H-102**  
  Gate: `FullyQualifiedName~SurfacingWizard`  
  AC: Creates a raster facing program for sheet XY; user must press Start.

- [ ] **H-403** Rotary mode (Y→A wrap at send time optional)  
  Locks: **MACHINE**, posts if needed  
  Parallel-OK: photo engine  
  Gate: `FullyQualifiedName~RotaryMode`  
  AC: Toggle documented; simulator accepts wrapped A or Y-as-A; no auto-start.

---

## Wave 5 — Easy power

- [ ] **H-501** Material + bit preset fills Cut params  
  Locks: **CUT**  
  Gate: `FullyQualifiedName~MaterialBitPreset`  
  AC: Pick Hardwood + 6mm EM → feed/plunge/rpm match DB; Calculate uses them.

- [ ] **H-502** Recipe: photo plaque / 3D coaster / sign  
  Locks: **APP-SHELL**  
  Depends: H-210, H-301  
  Gate: `FullyQualifiedName~FlashRecipes`  
  AC: Each recipe creates job + at least one toolpath ready to Calculate.

- [ ] **H-503** Live sim playback on same 3D view while streaming  
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

OWN files only:
{paths}

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
| Safe default | **2 workers:** one APP lock + one **new-file-only** ENGINE card |
| Never | Two of {CUT, APP-SHELL, MACHINE} |

**csproj rule:** only the worker whose OWN list includes `VectorPilot.Engine.csproj` / `VectorPilot.App.csproj` may add files. If the other needs a new `.cs`, they put it in a folder already in the glob, or wait.

---

## Done log

(orchestrator appends `H-xxx` + merge SHA)

- `H-000` — e946298 (clean main, AGENTS.md points here, README units claim corrected + 11 pinning tests)
