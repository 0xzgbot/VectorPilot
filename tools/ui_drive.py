"""Real UI automation for VectorPilot: pyautogui clicks + UIA reads + screenshots.

Verified capability, not a plan: dismisses the launch modal, enumerates the live
automation tree, clicks real controls, and captures pixels for inspection.
"""
import subprocess, sys, time, os

import pyautogui
import pygetwindow as gw

EXE = r"C:\Users\tmoph\OneDrive\Documents\cncresearch\VectorPilot\src\VectorPilot.App\bin\Debug\net8.0-windows\VectorPilot.exe"
SHOT_DIR = os.path.join(os.environ["LOCALAPPDATA"], "Temp")
pyautogui.FAILSAFE = False


def shot(name):
    p = os.path.join(SHOT_DIR, f"vp-{name}.png")
    pyautogui.screenshot(p)
    print(f"  shot: {p}")
    return p


def find_window(timeout=25):
    """Any VectorPilot-owned window, modal or main."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        for t in gw.getAllTitles():
            if not t.strip():
                continue
            if "VectorPilot" in t or "Recover unsaved work" in t:
                return gw.getWindowsWithTitle(t)[0]
        time.sleep(0.4)
    return None


def main():
    # Clean autosave state so the recovery modal does not gate startup.
    if "--clean" in sys.argv:
        ad = os.path.join(os.environ["LOCALAPPDATA"], "VectorPilot")
        for f in ("autosave.shoppilot", "autosave.json"):
            p = os.path.join(ad, f)
            if os.path.exists(p):
                os.remove(p)
                print(f"  removed {p}")

    print("== launch ==")
    proc = subprocess.Popen([EXE])
    time.sleep(2.5)

    win = find_window()
    if win is None:
        print("FAIL: no window appeared")
        proc.kill()
        return 1
    print(f"  window: '{win.title}'")

    # Dismiss the launch-blocking recovery modal with a real click.
    if "Recover" in win.title:
        print("== dismissing recovery modal (real click on 'No') ==")
        shot("modal")
        try:
            win.activate()
        except Exception:
            pass
        time.sleep(0.4)
        # 'No' sits at the lower-right of the dialog box.
        x = win.left + int(win.width * 0.72)
        y = win.top + int(win.height * 0.78)
        pyautogui.click(x, y)
        print(f"  clicked No at ({x},{y})")
        time.sleep(1.8)

        win = find_window()
        if win is None:
            print("FAIL: main window never appeared after dismissing modal")
            proc.kill()
            return 1
        print(f"  now: '{win.title}'")

    try:
        win.maximize()
    except Exception:
        pass
    time.sleep(1.2)
    shot("main")

    print("== live UIA tree ==")
    ps = r"""
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes | Out-Null
$AE=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]
$c=New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty,%d)
$w=$AE::RootElement.FindFirst($TS::Children,$c)
if(-not $w){"NOWINDOW";exit}
"WINDOW: " + $w.Current.Name
$all=$w.FindAll($TS::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
foreach($e in $all){
  $t=$e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
  if($t -in @('Button','RadioButton','CheckBox','ComboBox','Slider')){
    "{0}|{1}|{2}|{3}" -f $t,$e.Current.AutomationId,$e.Current.Name,$e.Current.IsEnabled
  }
}
""" % proc.pid
    out = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                         capture_output=True, text=True, timeout=90).stdout
    lines = [l for l in out.splitlines() if l.strip()]
    for l in lines[:60]:
        print("  " + l)
    print(f"  ({len(lines)-1} interactive controls)")

    proc.kill()
    return 0


if __name__ == "__main__":
    sys.exit(main())
