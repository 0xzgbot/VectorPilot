"""Drive the live VectorPilot app: real clicks, live UIA tree, screenshots.

The launch path is gated by a "Recover unsaved work" modal — dismissing it is why
this exists and why --ui-smoke hung. No NuGet, no FlaUI: PowerShell UIAutomation
for reads/invokes, pyautogui for the one click UIA can't reach.

  python tools/ui_verify.py          verify A5 + A6 reachability
  python tools/ui_verify.py --tree   dump the interactive tree and exit
"""
import os
import shutil
import subprocess
import sys
import time

import pyautogui
import pygetwindow as gw

EXE = (r"C:\Users\tmoph\OneDrive\Documents\cncresearch\VectorPilot"
       r"\src\VectorPilot.App\bin\Debug\net8.0-windows\VectorPilot.exe")
TMP = os.path.join(os.environ["LOCALAPPDATA"], "Temp")
APPDATA = os.path.join(os.environ["LOCALAPPDATA"], "VectorPilot")
INTERACTIVE = "'Button','RadioButton','CheckBox','ComboBox','Slider','Edit','List','Tree'"
pyautogui.FAILSAFE = False

_PREAMBLE = """
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes | Out-Null
$AE=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]
$c=New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty,%d)
$w=$AE::RootElement.FindFirst($TS::Children,$c)
if(-not $w){"NOWINDOW";exit}
$all=$w.FindAll($TS::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
"""


def ps(pid, body):
    """Run a UIA snippet against pid; decode defensively (stage buttons carry emoji)."""
    r = subprocess.run(["powershell", "-NoProfile", "-Command", _PREAMBLE % pid + body],
                       capture_output=True, timeout=90)
    return (r.stdout or b"").decode("utf-8", errors="replace")


def tree(pid):
    out = ps(pid, f"""
foreach($e in $all){{
  $t=$e.Current.ControlType.ProgrammaticName -replace 'ControlType\\.',''
  if($t -in @({INTERACTIVE})){{
    "{{0}}|{{1}}|{{2}}|{{3}}" -f $t,$e.Current.AutomationId,$e.Current.Name,$e.Current.IsEnabled
  }}
}}""")
    rows = [line.split("|") for line in out.splitlines() if line.count("|") == 3]
    return [{"type": t, "id": i, "name": n, "enabled": e.strip().lower() == "true"}
            for t, i, n, e in rows]


def invoke(pid, needle):
    """Click a control by name or automation id — no coordinate guessing."""
    out = ps(pid, f"""
foreach($e in $all){{
  if($e.Current.Name -like "*{needle}*" -or $e.Current.AutomationId -eq "{needle}"){{
    try{{ $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
          "INVOKED"; break }}
    catch{{ try{{ $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                 "SELECTED"; break }} catch{{ "NOPATTERN" }} }}
  }}
}}""").strip()
    return out.startswith(("INVOKED", "SELECTED"))


def launch():
    """Start the app, dismiss the recovery modal, maximize. Returns (proc, window)."""
    for f in os.listdir(APPDATA) if os.path.isdir(APPDATA) else []:
        if "autosave" in f.lower():
            p = os.path.join(APPDATA, f)
            shutil.rmtree(p, ignore_errors=True) if os.path.isdir(p) else os.remove(p)

    proc = subprocess.Popen([EXE])
    win = None
    for _ in range(50):
        time.sleep(0.4)
        for t in gw.getAllTitles():
            if "VectorPilot" in t or "Recover" in t:
                win = gw.getWindowsWithTitle(t)[0]
                break
        if win:
            break
    if win is None:
        proc.kill()
        raise RuntimeError("no window appeared")

    if "Recover" in win.title:          # modal gates startup; UIA can't reach it
        pyautogui.click(win.left + int(win.width * .72), win.top + int(win.height * .78))
        time.sleep(1.8)
        win = next((gw.getWindowsWithTitle(t)[0]
                    for t in gw.getAllTitles() if "VectorPilot" in t), None)
        if win is None:
            proc.kill()
            raise RuntimeError("main window never appeared after dismissing modal")

    try:
        win.maximize()
    except Exception:
        pass
    time.sleep(1.0)
    return proc, win


def shot(name):
    pyautogui.screenshot(p := os.path.join(TMP, f"vp-{name}.png"))
    return p


def main():
    proc, win = launch()
    print(f"window: '{win.title}'")
    ok = True
    try:
        if "--tree" in sys.argv:
            # Optional stage to navigate to first: --tree Model
            args = [a for a in sys.argv[2:] if not a.startswith("-")]
            if args:
                invoke(proc.pid, args[0])
                time.sleep(1.5)
            for r in tree(proc.pid):
                print(f"  {r['type']:<12} id={r['id']:<18} enabled={r['enabled']}  {r['name']}")
            return 0

        print("== A5: Machine stage ==")
        invoke(proc.pid, "Machine")
        time.sleep(1.5)
        shot("machine")
        ids = {r["id"]: r for r in tree(proc.pid) if r["id"]}

        for want in ("BtnEStop", "BtnReset", "ConsoleToggle", "BtnConnect", "BtnStart"):
            r = ids.get(want)
            print(f"  {want:<14} {'enabled=' + str(r['enabled']) if r else 'MISSING'}")
            ok &= r is not None

        # The invariant: E-STOP live while disconnected, and no auto-start.
        estop, start = ids.get("BtnEStop"), ids.get("BtnStart")
        estop_ok = bool(estop and estop["enabled"])
        start_ok = bool(start and not start["enabled"])
        print(f"  A5 estop-always-enabled: {'PASS' if estop_ok else 'FAIL'}")
        print(f"  A5 no-auto-start:        {'PASS' if start_ok else 'FAIL'}")
        ok &= estop_ok and start_ok

        if estop_ok:
            invoke(proc.pid, "BtnEStop")
            time.sleep(.8)
            print(f"  A5 survives real E-STOP click: {'PASS' if proc.poll() is None else 'FAIL'}")
            ok &= proc.poll() is None

        print("== A6: component tree, all stages ==")
        found = []
        for stage in ("Setup", "Design", "Model", "Toolpaths", "Machine", "Output"):
            invoke(proc.pid, stage)
            time.sleep(1.1)
            hits = [r["id"] or r["name"] for r in tree(proc.pid)
                    if r["type"] in ("List", "Tree")
                    or any(k in r["name"].lower() for k in ("combine", "sculpt"))]
            print(f"  {stage:<10} {hits or '—'}")
            found += [h for h in hits if "component" in h.lower() or "combine" in h.lower()]
        print(f"  A6 reachable: {'yes' if found else 'NO — confirmed unreachable'}")

        shot("final")
        print("VERDICT:", "PASS" if ok else "FAIL")
        return 0 if ok else 1
    finally:
        proc.kill()


if __name__ == "__main__":
    sys.exit(main())
