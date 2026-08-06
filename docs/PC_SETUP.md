# VectorPilot — PC Setup & Handoff Checklist

**Purpose:** bootstrap the VectorPilot Windows port on the dev PC in one session.
**Assumes:** Windows 10/11 x64, admin rights, internet.

---

## 1. Toolchain

| Tool | Why | Install |
|---|---|---|
| **Git** | repo + CI | `winget install Git.Git` |
| **.NET SDK 8 (LTS)** | the port runtime/compiler | `winget install Microsoft.DotNet.SDK.8` |
| **Visual Studio 2022 Community** (or JetBrains Rider) | WPF XAML designer + debugger | `winget install Microsoft.VisualStudio.2022.Community` — workload: **.NET desktop development** |
| **SQLite** (sqlite3 CLI or DB Browser) | inspect ppdb/vtdb evidence | `winget install SQLite.SQLite` |
| **7-Zip** | re-unpack installer evidence | `winget install 7zip.7zip` |
| **Node (optional)** | tooling scripts | `winget install OpenJS.NodeJS.LTS` |

Verify: `dotnet --version` → 8.x · `git --version` · open VS → create a WPF project → F5.

## 2. USB-serial drivers (for Phase 3+ hardware work)

Hobby CNC controllers commonly ship:
- **CH340/CH341** — `winget install CH341Ser` (or vendor driver; Windows Update often has it)
- **CP210x** — Silicon Labs CP210x VCP driver
- **FTDI FT232** — Windows Update handles it
Check Device Manager → Ports (COM & LPT) after plugging in the controller; note the COM port.

## 3. Repos

```powershell
# The Mac flagship stays at github.com/0xzgbot/ShopPilot (Swift) — read-only here, mostly
git clone https://github.com/0xzgbot/ShopPilot.git ~\dev\ShopPilot-mac   # reference + spec source

# NEW port repo (Phase 0 action — create on GitHub first)
git clone https://github.com/0xzgbot/VectorPilot.git ~\dev\ShopPilotWin
```

Suggested solution layout (stack decision A — see GAMEPLAN.md §2):

```
VectorPilot/
  VectorPilot.Engine/       # C# class lib — toolpaths, session, streamer, parser, posts, tool DB  (ports ShopPilotCore)
  VectorPilot.Geometry/     # C# class lib — vector kernel, DXF/SVG, text, trace                  (ports ShopPilotGeometry)
  VectorPilot.Serial/       # C# class lib — MachineTransport protocol + System.IO.Ports impl + simulator
  VectorPilot.App/          # WPF app — stage rail, canvas, machine panel, preview host
  VectorPilot.Tests/        # xUnit — the ported 97 verify CLTs + goldens (the DoD)
  assets/                    # JSON seeds (72 presets, tool DB), golden G-code, job-sheet template
  docs/                      # this plan, LIVE_CAPTURE.md, parity matrix
```

## 4. Regenerate the installer evidence (when needed)

The 545MB installer exe is at `~/Desktop/ShopPilot/.hermes/desktop-attachments/AspireTrialEdition_Setup.exe`
on the Mac (gitignored — copy it to the PC once, e.g. USB or network share). Then:

```powershell
7z x -y -o C:\dev\installer_unpacked AspireTrialEdition_Setup.exe
# post DB:    $APPDATA\Vectric\Aspire Trial Edition\V12.5\PostP\postp.ppdb   (SQLite: 800 posts + 935 machines)
# tool DBs:   ...\V12.5\ToolDatabase\*.vtdb                                 (SQLite, GUID 3-part linkage)
# defaults:   ...\V12.5\ToolpathDefaults\*.default                          (binary — semantics only, don't parse/copy)
# GRBL .pp:   sqlite3 postp.ppdb "SELECT f.content FROM PostPFile f JOIN PostPContent c ON f.postp_content_id=c.id JOIN PostPEntity e ON c.postp_ent_id=e.id WHERE e.name LIKE 'Grbl (mm)%' AND f.is_pp=1 LIMIT 1;"
```

Rules: **mirror field names/defaults/workflow, never copy formats/assets.** GRBL post grammar is
documented in `PORT_MANIFEST.md` §5 — the template engine mirrors the pattern, not the file.

## 5. Windows live capture (the highest-value first task on the PC)

1. Launch the installed reference trial → confirm identity (Help → About).
2. Paste `docs/planning/WINDOWS_EXPLORER_PROMPT.md` (in the Mac repo) into a Hermes session **on this PC**.
3. Output lands in `C:\Users\<you>\Desktop\capture-explore\` → deliver `LIVE_CAPTURE.md`.
4. Merge into the port kanban as the UI/UX acceptance reference (forms, defaults, menu leaves, trial limits).

## 6. CI

GitHub Actions, `windows-latest`: `dotnet restore && dotnet build -c Release && dotnet test`.
Keep the Mac repo's Swift CI untouched. The port repo's gate = **tests, not build**.

## 7. Notes

- `.shoppilot` documents are the interop contract — the JSON schema (from the Swift Codable models) is
  frozen in Phase 1; never break Mac↔Win file compat.
- The trial PC doubles as the UI reference: screenshots of every form are the acceptance criteria for
  Phase 4/5.
- No Apple notarization wall on Windows — packaging is Inno Setup + optional code-signing cert.
