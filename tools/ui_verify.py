"""Drive VectorPilot through its stages with real clicks and verify reachability.

Answers the questions the test suite cannot: does a user reach the Machine
stage, is E-STOP actually present and enabled in the live tree, and does
ComponentTreePanel appear anywhere a user can get to?
"""
import subprocess, sys, time, os
import pyautogui
import pygetwindow as gw

EXE = r"C:\Users\tmoph\OneDrive\Documents\cncresearch\VectorPilot\src\VectorPilot.App\bin\Debug\net8.0-windows\VectorPilot.exe"
TMP = os.path.join(os.environ["LOCALAPPDATA"], "Temp")
pyautogui.FAILSAFE = False

PS_TREE = r"""
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes | Out-Null
$AE=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]
$c=New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty,%d)
$w=$AE::RootElement.FindFirst($TS::Children,$c)
if(-not $w){"NOWINDOW";exit}
$all=$w.FindAll($TS::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
foreach($e in $all){
  $t=$e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
  if($t -in @('Button','RadioButton','CheckBox','ComboBox','Slider','Edit','List','Tree')){
    "{0}|{1}|{2}|{3}" -f $t,$e.Current.AutomationId,$e.Current.Name,$e.Current.IsEnabled
  }
}
"""


def _ps(script):
    """Run PowerShell and decode defensively — stage buttons carry emoji."""
    r = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                       capture_output=True, timeout=90)
    return (r.stdout or b"").decode("utf-8", errors="replace")


def tree(pid):
    out = _ps(PS_TREE % pid)
    rows = []
    for line in out.splitlines():
        parts = line.split("|")
        if len(parts) == 4:
            rows.append({"type": parts[0], "id": parts[1], "name": parts[2],
                         "enabled": parts[3].strip().lower() == "true"})
    return rows


def click_by_name(pid, needle):
    """Invoke a control by its UIA name — no coordinate guessing."""
    ps = r"""
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes | Out-Null
$AE=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]
$c=New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty,%d)
$w=$AE::RootElement.FindFirst($TS::Children,$c)
if(-not $w){"NOWINDOW";exit}
$all=$w.FindAll($TS::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
foreach($e in $all){
  if($e.Current.Name -like "*%s*" -or $e.Current.AutomationId -eq "%s"){
    try{
      $p=$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
      $p.Invoke(); "INVOKED: " + $e.Current.Name; break
    }catch{
      try{
        $p=$e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $p.Select(); "SELECTED: " + $e.Current.Name; break
      }catch{ "NOPATTERN: " + $e.Current.Name }
    }
  }
}
""" % (pid, needle, needle)
    out = _ps(ps).strip()
    print(f"  {out or 'not found: ' + needle}")
    return out.startswith(("INVOKED", "SELECTED"))


def main():
    # Remove autosave so the recovery modal does not gate startup.
    ad = os.path.join(os.environ["LOCALAPPDATA"], "VectorPilot")
    for f in os.listdir(ad) if os.path.isdir(ad) else []:
        if "autosave" in f.lower():
            os.remove(os.path.join(ad, f))
            print(f"  cleared {f}")

    proc = subprocess.Popen([EXE])
    time.sleep(3.0)

    win = None
    for _ in range(40):
        for t in gw.getAllTitles():
            if "VectorPilot" in t or "Recover" in t:
                win = gw.getWindowsWithTitle(t)[0]
                break
        if win:
            break
        time.sleep(0.4)
    if not win:
        print("FAIL: no window")
        proc.kill()
        return 1

    if "Recover" in win.title:
        pyautogui.click(win.left + int(win.width * 0.72), win.top + int(win.height * 0.78))
        time.sleep(1.8)
        for t in gw.getAllTitles():
            if "VectorPilot" in t:
                win = gw.getWindowsWithTitle(t)[0]
    try:
        win.maximize()
    except Exception:
        pass
    time.sleep(1.0)

    results = {}

    print("== stage: Machine (A5) ==")
    click_by_name(proc.pid, "Machine")
    time.sleep(1.5)
    pyautogui.screenshot(os.path.join(TMP, "vp-machine.png"))
    rows = tree(proc.pid)
    ids = {r["id"]: r for r in rows if r["id"]}
    for want in ("BtnEStop", "BtnReset", "ConsoleToggle", "BtnConnect", "BtnStart"):
        r = ids.get(want)
        results[want] = r
        print(f"  {want:<14} {'FOUND enabled=' + str(r['enabled']) if r else 'MISSING'}")

    # The A5 invariant, tested on the live control: E-STOP enabled while disconnected.
    estop = ids.get("BtnEStop")
    if estop:
        print(f"  A5 INVARIANT (E-STOP enabled while disconnected): "
              f"{'PASS' if estop['enabled'] else 'FAIL'}")
        print("  clicking E-STOP for real...")
        click_by_name(proc.pid, "BtnEStop")
        time.sleep(0.8)
        print(f"  app still alive: {proc.poll() is None}")

    print("== searching every stage for a component tree (A6) ==")
    found_a6 = False
    for stage in ("Setup", "Design", "Toolpaths", "Machine", "Output"):
        click_by_name(proc.pid, stage)
        time.sleep(1.1)
        rows = tree(proc.pid)
        hits = [r for r in rows if r["type"] in ("List", "Tree")
                or "combine" in r["name"].lower() or "sculpt" in r["name"].lower()]
        if hits:
            print(f"  {stage}: {[h['name'] or h['id'] or h['type'] for h in hits][:5]}")
            found_a6 = True
    if not found_a6:
        print("  A6: NO component tree reachable from ANY stage — confirmed unreachable")

    pyautogui.screenshot(os.path.join(TMP, "vp-final.png"))
    proc.kill()
    print("DONE")
    return 0


if __name__ == "__main__":
    sys.exit(main())
