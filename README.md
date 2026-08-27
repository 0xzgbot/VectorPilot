# VectorPilot

**Native Windows CNC suite** — design vector art, generate 2.5D and 3D toolpaths, preview the cut, and stream it to the machine. C# / .NET 8 + WPF, with a DirectX 11 preview.

> 🛑 **Safety first:** VectorPilot is a CAM + machine-control app. The built-in simulator and the 3D preview are rehearsal tools, not a replacement for a hardware e-stop. Simulate, then air-cut, then cut. Wire a physical stop to your controller.

[![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-0078D6)](#build-from-source)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4)](#build-from-source)

---

## What it is

VectorPilot is the Windows sibling of the macOS [ShopPilot](https://github.com/0xzgbot/ShopPilot) CNC suite, ported to C# and WPF. It covers the same path end to end: import or draw a design, lay out toolpaths, check the cut in a DirectX preview, then drive a GRBL-style machine. Jobs save as `.shoppilot` packages (manifest, sheets, toolpaths), so a document can move between the Mac and Windows apps.

It is for hobby and small-shop CNC router users who want design, CAM, and machine control in one native app, without a cloud service.

---

## What it does

**Import:** DXF, SVG, EPS, PDF, AI, DWG, STL, OBJ, 3MF, bitmap trace, grayscale reliefs, and cabinetry part lists (Mozaik, KCD, CabinetSense, CabinetPartsPro, Polyboard, SmartWOP, plus a generic JSON mapping).

**Export:** DXF, STL, OBJ, EPS, PDF, grayscale bitmaps, and `.tap` through the post catalog.

**Toolpaths:** Profile, Pocket, V-carve, Drill (and drill bank), Quick Engrave, Prism, Fluting, Chamfer, Bevel, Drag knife, Texture, Inlay, Laser cut/fill/picture, 3D rough and finish, Photo V-carve, Sketch carving, and moulding. Shared: leads, tabs, ramps, keep-out zones, tiling, nesting, templates, sort/merge, array copy, and rotary wrap.

**3D:** heightfields from mesh or grayscale, a component tree with combine modes, sculpt, 2-rail sweep, weave reliefs, rough/finish machining, a material-removal simulator, and a WPF 3D (DirectX 11) preview.

**Machine:** GRBL-style transport, a built-in simulator, streaming with feed and speed overrides, pause/resume, and a preflight gate (thickness, keep-out, and open-path checks). Posts cover common routers, industrial controls, laser, and plasma. Job sheets print as HTML/PDF.

**App:** a stage rail (Setup → Design → Model → Toolpaths → Machine → Output), an Inno Setup installer, and GitHub Actions that build and test on every push.

---

## Build from source

Requires the **.NET 8 SDK** (Windows 10/11 x64). From a repo root:

```bash
dotnet restore VectorPilot.sln
dotnet build VectorPilot.sln -c Release -warnaserror
dotnet test VectorPilot.sln -c Release --no-build
```

The repo's canonical gate is `./verify.sh`: Release build, zero warnings, full suite, plus an optional xUnit filter.

```bash
./verify.sh
./verify.sh "FullyQualifiedName~CanvasEditing"
```

Kill a running `VectorPilot.exe` before building; a running app locks the DLLs (`MSB3021`).

To publish the app the way the installer expects it (self-contained win-x64):

```bash
dotnet publish src/VectorPilot.App/VectorPilot.App.csproj -c Release -r win-x64 --self-contained
```

CI builds, tests, and runs a publish smoke-check on every push to `main`. Tagging `v*` runs the release workflow, which builds the installer with Inno Setup into `dist/`.

---

## Connecting a machine

- **Simulator:** port `SIMULATOR`, no driver. CI covers the simulator path; a physical controller is not part of the automated gate.
- **GRBL:** CH340/FTDI VCP driver, pick the COM port, 115200 baud. The machine profile chooses G21 (mm) or G20 (inch).
- Confirm the work envelope before the first run. Preflight and keep-out checks run before the stream starts.
- Software is not a hardware e-stop. Wire a physical stop to the controller.

The installer registers `.shoppilot` files.

---

## Documentation

| File | Contents |
| --- | --- |
| [`docs/PC_SETUP.md`](docs/PC_SETUP.md) | Windows toolchain, USB-serial drivers, environment setup |
| [`docs/FEATURES.md`](docs/FEATURES.md) | Notes on the algorithms and honest non-goals |
| [`docs/PORT_MANIFEST.md`](docs/PORT_MANIFEST.md) | File-by-file inventory of the port from ShopPilot |
| [`docs/spec/`](docs/spec/) | Document schema, presets, tool database, golden G-code |

---

## Status

VectorPilot is a working nightly port. The engine core is shipped: geometry, import/export, the toolpath strategies above, 3D rough/finish, the simulator, preflight, and posts are implemented with a green xUnit suite and golden G-code files. The WPF shell runs the stage rail, design canvas, toolpath tree, machine panel, and a DirectX preview.

**Held / not done:** V3M, SketchUp `.skp`, and Rhino `.3dm` import are reserved until a public spec or SDK exists; the import stubs report an honest "not implemented" rather than a fake parser. Live machine control is only exercised through the simulator on CI; hardware testing is manual. Packaging (code signing, auto-update) is not finished either. The project is a port in progress, not a finished release.

---

## License

No LICENSE file is committed yet. Like the macOS sibling, VectorPilot is personal-use only, never for sale, and all code is written from scratch, with no third-party proprietary assets.
