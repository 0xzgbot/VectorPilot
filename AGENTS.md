# VectorPilot — agent operating rules

Port of macOS ShopPilot to Windows (C#/.NET 8 + WPF). Mac source of truth, read-only:
`../ShopPilot/Sources/**`. Match semantics exactly — identical numbers, not approximations.

## Hard rules

1. **No status reports.** Do not write summary tables, "where we stand", or progress
   recaps. The user has explicitly said they have no time for it. Ship code; the
   commit log is the report.
2. **No stopping between cards.** Finish a card, commit, take the next one from
   `PARITY_QUEUE.md`. Never end a turn with "next up is X" — just do X.
3. **A card is not `[x]` until a UI element invokes it.** Grep the panel
   `.xaml`/`.xaml.cs` for a real call-site. A class only tests import is NOT done.
   This rule has been violated on A1, A5, and A6 — check yourself before claiming.
4. **UI first, tests second.** The engine is 14.8k LOC; the app is the bottleneck.
   Never add an engine class when the gap is wiring.
5. **`./verify.sh [filter]` is the only gate.** Never hand-roll a verification
   script for the solution. It enforces Release + zero warnings and fails on
   empty filters and `MSB302x` locks.
6. **Do not delegate.** 7/7 subagents on this key died to HTTP 429 without
   writing a file. Do the work directly.
7. **Test counts are not progress.** A green filter on an unreachable class is
   make-work. Prefer one wired feature over ten tested-but-orphaned classes.

## Environment

- `dotnet` is NOT on PATH: `export PATH="$PATH:/c/Program Files/dotnet"`
- Kill `VectorPilot.exe` before building; a running app locks the DLLs (`MSB3021`).
- Shell is git-bash. Native tools need `C:/...` paths, not `/c/...`.
- Real UI automation works: `python tools/ui_verify.py` (UIA + pyautogui).
  Use `--automated` to suppress startup modals.

## Known lies to fix, not repeat

- Pocket: FIXED — now scanline-clipped to the outline (was bounding-box raster).
- V-carve: FIXED — depth now derives from local channel width (VCarveGeometry); the Y-position golden was regenerated.
- Weave: FIXED — WeaveReliefGenerator emits a real interlaced heightfield; the estimator remains for cost/time only.
- `MASTER_KANBAN.md` and `GAMEPLAN-ASPIRE-PARITY.md` are stale. `PARITY_QUEUE.md`
  is current.
