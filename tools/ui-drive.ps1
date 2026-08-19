# Real UI automation for VectorPilot via Windows UIAutomation (no NuGet).
# Launches the app, enumerates the live automation tree, clicks real controls,
# and captures a screenshot of the actual window.
param(
  [string]$Exe = "C:/Users/tmoph/OneDrive/Documents/cncresearch/VectorPilot/src/VectorPilot.App/bin/Debug/net8.0-windows/VectorPilot.exe",
  [string]$Shot = "C:/Users/tmoph/AppData/Local/Temp/vp-ui.png"
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing | Out-Null
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
"@ 2>$null | Out-Null

function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($TS::Descendants, $c)
}

Write-Output "== launching =="
$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Milliseconds 1200

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
$win = $null
for ($i = 0; $i -lt 40 -and $null -eq $win; $i++) {
  $win = $AE::RootElement.FindFirst($TS::Children, $cond)
  if ($null -eq $win) { Start-Sleep -Milliseconds 400 }
}
if ($null -eq $win) { Write-Output "FAIL: no window for pid $($proc.Id)"; $proc.Kill(); exit 1 }

Write-Output "window: '$($win.Current.Name)'  pid=$($proc.Id)"
[void][W]::ShowWindow([IntPtr]$win.Current.NativeWindowHandle, 3)  # maximize
[void][W]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle)
Start-Sleep -Milliseconds 700

Write-Output ""
Write-Output "== interactive controls in the live tree =="
$all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$n = 0
foreach ($e in $all) {
  $t = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''
  if ($t -in @('Button','RadioButton','CheckBox','ComboBox','Edit','Slider')) {
    $n++
    Write-Output ("  {0,-12} id={1,-18} name='{2}' enabled={3}" -f `
      $t, $e.Current.AutomationId, $e.Current.Name, $e.Current.IsEnabled)
  }
}
Write-Output "  ($n interactive controls)"

Write-Output ""
Write-Output "== A5/A6 reachability assertions against the LIVE tree =="
foreach ($id in @('BtnEStop','BtnReset','ConsoleToggle','BtnConnect','BtnStart')) {
  $el = ById $win $id
  if ($null -eq $el) { Write-Output "  MISSING  $id" }
  else { Write-Output ("  found    {0,-14} enabled={1}" -f $id, $el.Current.IsEnabled) }
}

Write-Output ""
Write-Output "== clicking E-STOP for real =="
$estop = ById $win 'BtnEStop'
if ($estop) {
  try {
    $ip = $estop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke()
    Start-Sleep -Milliseconds 600
    Write-Output "  invoked; app alive = $(-not $proc.HasExited)"
  } catch { Write-Output "  invoke failed: $($_.Exception.Message)" }
}

Write-Output ""
Write-Output "== screenshot =="
$r = $win.Current.BoundingRectangle
$bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
$bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "  saved $Shot ($([int]$r.Width)x$([int]$r.Height))"

$proc.Kill()
Write-Output "DONE"
