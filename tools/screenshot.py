"""Screenshot the VectorPilot main window to a PNG (Release app, --automated).

Usage: python tools/screenshot.py <out.png> [stage]
  stage: setup|design|model|cut|machine|output  (default: current stage)
"""
import sys
import time

import pyautogui
import pygetwindow as gw

_STAGE_BUTTON = {
    "setup": "Setup", "design": "Design", "model": "Model",
    "cut": "Toolpaths", "machine": "Machine", "output": "Output",
}


def main() -> None:
    out = sys.argv[1] if len(sys.argv) > 1 else "vp-window.png"
    stage = sys.argv[2] if len(sys.argv) > 2 else None

    wins = [w for w in gw.getAllWindows()
            if "VectorPilot" in (w.title or "") and w.visible]
    if not wins:
        print("NO WINDOW")
        sys.exit(2)

    win = wins[0]
    try:
        win.activate()
        time.sleep(0.4)
    except Exception:
        pass  # activation is best-effort; the window may already be foreground

    if stage and stage in _STAGE_BUTTON:
        # Click the rail button by screen position inside the left rail column.
        # The rail buttons start ~y+120 from the window top and are spaced ~34px.
        import pyautogui as pag
        x = win.left + 95          # centre of the 190px rail
        labels = ["Setup", "Design", "Model", "Toolpaths", "Machine", "Output"]
        idx = labels.index(_STAGE_BUTTON[stage])
        y = win.top + 150 + idx * 36   # first button ~150px down (title + margin)
        pag.click(x, y)
        time.sleep(0.6)

    pyautogui.screenshot(out)
    print("SAVED", out)


if __name__ == "__main__":
    main()
