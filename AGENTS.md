# VectorPilot — Agent Operating Manual

> Point any local Hermes (or other) agent at this file to begin work.
> This repo is the **Windows port** of the ShopPilot CNC suite (new app: **VectorPilot**).

| Field | Value |
| --- | --- |
| **Project root** | this repo (clone: `github.com/0xzgbot/VectorPilot`) |
| **Product** | VectorPilot — native Windows CNC suite: design + toolpaths + preview + machine control (GRBL/FluidNC) |
| **Stack** | C#/.NET 8 · WPF · DirectX 11 · System.IO.Ports · xUnit |
| **Plan** | [`PLAN.md`](./PLAN.md) — milestones M0–M7, exit gates, risks. **Read it first.** |
| **Task board** | [`MASTER_KANBAN.md`](./MASTER_KANBAN.md) — claim work here |
| **Mac flagship** | `github.com/0xzgbot/ShopPilot` (Swift) — clone read-only; **the engine-semantics authority** |
| **DoD** | Ported verify harness green (identical numbers) + `dotnet build`; NOT build alone |
| **Last updated** | 2026-08-06 |

## Startup protocol

1. Read [`PLAN.md`](./PLAN.md) §1–2 (mission, DoD), §7 (principles), §8 (milestones).
2. Open [`MASTER_KANBAN.md`](./MASTER_KANBAN.md); claim `[ ]` → `[~]` + append worklog.
3. Clone the Mac repo read-only for semantics: `git clone https://github.com/0xzgbot/ShopPilot.git ../ShopPilot-mac` (do not push to it).
4. Implement **Engine + Data + Harness + (UI when milestone calls)** per card. Run the ported xUnit tests — green is the gate.
5. Mark `[x]` + worklog. Never mark `[x]` on build-only. No stub-file landings.

## Rules

- **Harness-gated:** a card is done when its verify tests pass with identical numbers to the Mac CLTs, and goldens match byte-for-byte.
- **Anti-loop:** 3+ similar failures on one card → change strategy, don't retry the same approach.
- **Safety (product requirements, ported verbatim):** e-stop/reset always visible; no auto-start streaming; disconnect/port error → stop + alarm; raw TX/RX console toggle; software is not a substitute for a hardware e-stop. No live machine motion without explicit user consent per action.
- **Simulator-first:** implement `IMachineTransport` + `SimulatorTransport` before any hardware path.
- **Commit per card:** `git add -A` (scoped to your files) + push. Never commit files a sibling left dirty.
- **Parallelism:** milestones run sequentially in-session (shared wiring files, one compile lock). Delegate only disjoint-file work.

## Status legend

`[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked on human-only action · `[-]` cancelled/deferred
