# VectorPilot

Windows CNC software: design vectors, generate toolpaths, preview the cut, and stream to the machine.

Native C# / .NET 8 + WPF. Jobs save as `.shoppilot` packages (manifest, sheets, toolpaths).

## What it does

**Import** DXF, SVG, EPS, PDF, AI, DWG, STL, OBJ, 3MF, bitmap trace, grayscale reliefs, and cabinetry part lists (Mozaik, KCD, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP, plus a generic JSON mapping).

**Export** DXF, STL, OBJ, EPS, PDF, grayscale bitmaps, `.tap` via the post catalog.

**Toolpaths** — Profile, Pocket, V-carve, Drill (and drill bank), Quick Engrave, Prism, Fluting, Chamfer, Bevel, Drag knife, Texture, Inlay, Laser cut/fill/picture, 3D rough and finish, Photo V-carve, Sketch carving, Moulding, plus leads, tabs, ramps, keep-out zones, tiling, nesting, templates, sort/merge, array copy, and rotary wrap.

**3D** — Heightfields from mesh or grayscale, a component tree with combine modes, sculpt, 2-rail sweep, weave reliefs, rough/finish, a material-removal simulator, and a WPF 3D preview.

**Machine** — GRBL-style transport, a built-in simulator, streaming with feed/speed overrides, pause/resume, and preflight (thickness, keep-out, open-path gates). Posts cover common routers, industrial controls, laser, and plasma. Job sheets print as HTML/PDF.

**App** — Stages: Setup → Design → Model → Toolpaths → Machine → Output. Inno Setup installer; GitHub Actions builds and tests on every push.

V3M, SketchUp `.skp`, and Rhino `.3dm` import are reserved until a public spec or SDK is available. Physical hardware is not covered by CI — use the simulator, then a dry run.

## Build

```bash
./verify.sh
```

Release build, zero warnings, full test suite. Kill `VectorPilot.exe` first if a running app has the DLLs locked.

## Connecting a machine

- **Simulator** — port `SIMULATOR`, no driver.
- **GRBL** — CH340/FTDI VCP driver, pick the COM port, 115200 baud. The machine profile chooses G21 (mm) or G20 (inch).
- Confirm the work envelope before the first run. Preflight and keep-out run before stream start.
- Software is not a hardware e-stop. Wire a physical stop to the controller.

The installer registers `.shoppilot` files.
