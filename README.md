# VectorPilot

**Native Windows CNC suite** — design → toolpaths → 3D preview → machine control (GRBL/FluidNC).
Sibling of **ShopPilot** (macOS, SwiftUI). Same product family, same `.shoppilot` document schema, independent codebase.

| | |
|---|---|
| **Stack** | C#/.NET 8 · WPF · DirectX 11 (HelixToolkit.SharpDX) · System.IO.Ports |
| **Repo status** | Planning (M0 ready) — see [PLAN.md](PLAN.md) |
| **Portability** | Engine logic translated from the ShopPilot Swift codebase; the ported verify harness is the definition of done |
| **Docs** | [PLAN.md](PLAN.md) (conversion plan) · [AGENTS.md](AGENTS.md) (agent manual) · [MASTER_KANBAN.md](MASTER_KANBAN.md) (task board) · [docs/PC_SETUP.md](docs/PC_SETUP.md) · [docs/PORT_MANIFEST.md](docs/PORT_MANIFEST.md) |

## Current status

- **M0 — Foundation:** plan published; next: spec pack (schema/seeds/goldens from the Mac repo), solution scaffold, Windows live-capture merge.
- **Nothing implemented yet** — this repo is the working surface for the conversion.

## Definition of done

1. Installable Windows app running: job setup → 2D design → toolpaths → 3D preview → post → machine control.
2. Ported verify harness green (identical numbers vs the Mac engine) + golden G-code files.
3. `.shoppilot` documents round-trip between ShopPilot and VectorPilot.
4. Safety bar verbatim: e-stop always visible, no auto-start streaming, disconnect → stop + alarm.
