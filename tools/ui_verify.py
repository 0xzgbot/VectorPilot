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


def combo_items(pid, automation_id):
    """Expand a ComboBox by AutomationId and list its selectable items."""
    out = ps(pid, f"""
foreach($e in $all){{
  if($e.Current.AutomationId -eq "{automation_id}"){{
    try{{
      $x = $e.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
      $x.Expand(); Start-Sleep -Milliseconds 500
      $c = New-Object System.Windows.Automation.PropertyCondition(
             [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
             [System.Windows.Automation.ControlType]::ListItem)
      foreach($i in $e.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){{
        "ITEM: " + $i.Current.Name
      }}
      $x.Collapse()
    }}catch{{ "NOEXPAND" }}
    break
  }}
}}""")
    return [l.split("ITEM: ", 1)[1].strip()
            for l in out.splitlines() if l.strip().startswith("ITEM: ")]


def select_combo_item(pid, automation_id, item_name):
    """Expand a ComboBox and select the item whose name matches."""
    out = ps(pid, f"""
foreach($e in $all){{
  if($e.Current.AutomationId -eq "{automation_id}"){{
    try{{
      $x = $e.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
      $x.Expand(); Start-Sleep -Milliseconds 400
      $c = New-Object System.Windows.Automation.PropertyCondition(
             [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
             [System.Windows.Automation.ControlType]::ListItem)
      foreach($i in $e.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){{
        if($i.Current.Name -eq "{item_name}"){{
          $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
          "SELECTED"; break
        }}
      }}
      $x.Collapse()
    }}catch{{ "NOSELECT" }}
    break
  }}
}}""").strip()
    return "SELECTED" in out


def launch():
    """Start the app, dismiss the recovery modal, maximize. Returns (proc, window)."""
    for f in os.listdir(APPDATA) if os.path.isdir(APPDATA) else []:
        if "autosave" in f.lower():
            p = os.path.join(APPDATA, f)
            shutil.rmtree(p, ignore_errors=True) if os.path.isdir(p) else os.remove(p)

    # --automated sets App.IsAutomated, which suppresses the first-run welcome and
    # the recovery prompt. Without it the harness inspects a modal, not the shell.
    proc = subprocess.Popen([EXE, "--automated"])
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
        if "--params" in sys.argv:
            # --params <StrategyDisplayName>  → Design: draw a rect, Toolpaths: select
            # that strategy, add a toolpath, then dump the params rows.
            args = [a for a in sys.argv[2:] if not a.startswith("-")]
            want = args[0] if args else "Weave"

            invoke(proc.pid, "Design")
            time.sleep(1.2)
            invoke(proc.pid, "Rect")
            time.sleep(0.6)
            # Drag a rectangle onto the canvas so a toolpath has source geometry.
            try:
                import pyautogui
                x0 = win.left + int(win.width * 0.45)
                y0 = win.top + int(win.height * 0.45)
                pyautogui.moveTo(x0, y0, duration=0.2)
                pyautogui.dragTo(x0 + 160, y0 + 110, duration=0.5, button="left")
                time.sleep(0.6)
            except Exception as exc:              # pragma: no cover - environment dependent
                print(f"  (drag skipped: {exc})")

            invoke(proc.pid, "Toolpaths")
            time.sleep(1.2)
            if not select_combo_item(proc.pid, "CmbStrategy", want):
                print(f"  could not select '{want}'")
            time.sleep(0.5)
            invoke(proc.pid, "+ Add Toolpath")
            time.sleep(1.0)

            rows = ps(proc.pid, """
foreach($e in $all){
  if($e.Current.AutomationId -eq "ParamsGrid"){
    $c = New-Object System.Windows.Automation.PropertyCondition(
           [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
           [System.Windows.Automation.ControlType]::Edit)
    foreach($i in $e.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c)){
      "EDIT: " + $i.Current.Name + "=" + $i.Current.AutomationId
    }
    $c2 = New-Object System.Windows.Automation.PropertyCondition(
           [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
           [System.Windows.Automation.ControlType]::ComboBox)
    foreach($i in $e.FindAll([System.Windows.Automation.TreeScope]::Descendants,$c2)){
      "DROPDOWN: " + $i.Current.Name
    }
    $t = New-Object System.Windows.Automation.PropertyCondition(
           [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
           [System.Windows.Automation.ControlType]::Text)
    foreach($i in $e.FindAll([System.Windows.Automation.TreeScope]::Descendants,$t)){
      "LABEL: " + $i.Current.Name
    }
    break
  }
}""")
            labels = [l.strip() for l in rows.splitlines() if l.strip()]
            print(f"  {want} params form: {len(labels)} controls")
            for l in labels:
                print(f"    {l}")
            return 0

        if "--combo" in sys.argv:
            # --combo Toolpaths CmbStrategy  → navigate, then list the combo's items
            args = [a for a in sys.argv[2:] if not a.startswith("-")]
            if len(args) < 2:
                print("usage: ui_verify.py --combo <Stage> <AutomationId>")
                return 2
            invoke(proc.pid, args[0])
            time.sleep(1.5)
            items = combo_items(proc.pid, args[1])
            print(f"  {args[1]}: {len(items)} items")
            for it in items:
                print(f"    - {it}")
            return 0

        if "--tree" in sys.argv:
            # Optional stage to navigate to first: --tree Model
            # Optional extra control to click before dumping: --tree Setup RbDouble
            args = [a for a in sys.argv[2:] if not a.startswith("-")]
            if args:
                invoke(proc.pid, args[0])
                time.sleep(1.5)
            for extra in args[1:]:
                invoke(proc.pid, extra)
                time.sleep(1.0)
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
