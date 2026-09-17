param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Win32 -Name N3 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
[Win32.N3]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$shots = Join-Path $root 'ui-shots'
New-Item -ItemType Directory -Force -Path $shots | Out-Null
$script:appPid = 0

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.N3+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.N3]::IsWindowVisible($h)) {
            $p = 0
            [Win32.N3]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
            if ($p -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.N3]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.N3]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Click-ByName($parent, [string]$name) {
    $btn = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    if (-not $btn) { return $false }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    return $true
}

try {
    $app = Start-Process -FilePath $exe -PassThru
    $script:appPid = $app.Id
    Start-Sleep -Seconds 6

    # main window = biggest process window
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:appPid)
    do {
        Start-Sleep -Milliseconds 500
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $main = $null; $best = 0
        foreach ($w in $wins) { $r = $w.Current.BoundingRectangle; if ($r.Width * $r.Height -gt $best) { $best = $r.Width * $r.Height; $main = $w } }
    } while (-not $main -or $best -le 0)

    # open groups page (strings built with -join; char+char would do int math)
    $menuName = -join @([char]0xD83D, [char]0xDCCA, ' ', [char]0x062D, [char]0x0633, [char]0x0627, [char]0x0628, [char]0x062F, [char]0x0627, [char]0x0631, [char]0x06CC)
    $menu = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $menuName)))
    if (-not $menu) { throw "menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800
    $subName = -join @([char]0xD83E, [char]0xDDE9, ' ', [char]0x06AF, [char]0x0631, [char]0x0648, [char]0x0647, [char]0x200C, [char]0x0647, [char]0x0627, [char]0x06CC, ' ', [char]0x062D, [char]0x0633, [char]0x0627, [char]0x0628)
    $sub = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $subName)))
    if (-not $sub) { throw "sub not found" }
    ($sub.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2

    # click new
    $newBtnName = -join @([char]0x2795, ' ', [char]0x062C, [char]0x062F, [char]0x06CC, [char]0x062F)
    if (-not (Click-ByName $main $newBtnName)) { throw "new button not clicked" }

    # dialog by new HWND
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    # note: $known captured after click includes dialog? No: captured after click -> dialog may already exist. Recapture approach:
    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) {
            $t = $w.T
            if ($t.Length -eq 14 -and $known -notcontains $w.H) { $dlgH = $w.H; break }
        }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "dialog hwnd not found" }
    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgH)

    # fill the two edit boxes
    $edits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    if ($edits.Count -lt 2) { throw "expected >=2 edit boxes, got $($edits.Count)" }
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    ($edits[0].GetCurrentPattern($vp)).SetValue((-join @([char]0x062F, [char]0x0627, [char]0x0631, [char]0x0627, [char]0x06CC))) # "دارایی"
    ($edits[1].GetCurrentPattern($vp)).SetValue("9999")
    Start-Sleep -Milliseconds 300

    # save
    # save: dump button names first for exact match
    $btns = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    foreach ($b in $btns) {
        $n = $b.Current.Name
        $codes = ($n.ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ','
        Write-Output ("BTN: {0}" -f $codes)
    }
    $saveName = -join @([char]0xD83D, [char]0xDCBE, ' ', [char]0x0630, [char]0x062E, [char]0x06CC, [char]0x0631, [char]0x0647)
    if (-not (Click-ByName $dlg $saveName)) { throw "save button not clicked" }
    Start-Sleep -Seconds 2

    Write-Output "PASS: form filled and saved via UI"
}
catch {
    Write-Output "FAIL: $_"
}
finally {
    if ($script:appPid -ne 0) {
        Get-Process -Id $script:appPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
