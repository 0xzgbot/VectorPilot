# VectorPilot — PC Setup

**Purpose:** bootstrap VectorPilot on a Windows 10/11 x64 machine.

---

## 1. Toolchain

| Tool | Why | Install |
|---|---|---|
| **Git** | repo + CI | `winget install Git.Git` |
| **.NET SDK 8 (LTS)** | runtime/compiler | `winget install Microsoft.DotNet.SDK.8` |
| **Visual Studio 2022 Community** (or JetBrains Rider) | WPF designer + debugger | `winget install Microsoft.VisualStudio.2022.Community` — workload: **.NET desktop development** |

Verify: `dotnet --version` → 8.x · `git --version`.

## 2. USB-serial drivers

Hobby CNC controllers commonly ship:
- **CH340/CH341** — Windows Update often has it
- **CP210x** — Silicon Labs VCP driver
- **FTDI FT232** — Windows Update handles it

Check Device Manager → Ports (COM & LPT) after plugging in the controller; note the COM port.

## 3. Repo

```powershell
git clone https://github.com/0xzgbot/VectorPilot.git ~\dev\VectorPilot
```

```
VectorPilot/
  src/VectorPilot.Engine/
  src/VectorPilot.Geometry/
  src/VectorPilot.Serial/
  src/VectorPilot.App/
  tests/VectorPilot.Tests/
  docs/
```

## 4. CI

GitHub Actions, `windows-latest`: Release build, `-warnaserror`, full suite, publish smoke. Local gate is `./verify.sh`.

## 5. Notes

- `.shoppilot` documents are the job package format.
- Packaging is Inno Setup + optional code-signing.
