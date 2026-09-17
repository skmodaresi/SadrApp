param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Win32 -Name N4 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
[Win32.N4]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$script:appPid = 0

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.N4+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.N4]::IsWindowVisible($h)) {
            $p = 0
            [Win32.N4]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
            if ($p -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.N4]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.N4]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function U([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }

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

    # ---- 1) Login window ----
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:appPid)
    $login = $null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and -not $login) {
        Start-Sleep -Milliseconds 400
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins) { if ($w.Current.BoundingRectangle.Width -gt 0) { $login = $w; break } }
    }
    if (-not $login) { throw "login window not found" }

    $edits = $login.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    if ($edits.Count -lt 2) { throw "login edits not found ($($edits.Count))" }
    # Username = plain edit; password = the IsPassword edit (collapsed reveal box is not in the UIA tree).
    $userEl = $null; $pwdEl = $null
    foreach ($e in $edits) {
        if ($e.Current.IsPassword) { $pwdEl = $e } else { $userEl = $e }
    }
    if (-not $userEl -or -not $pwdEl) { throw "login fields not identified" }
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    ($userEl.GetCurrentPattern($vp)).SetValue("admin")
    ($pwdEl.GetCurrentPattern($vp)).SetValue("admin")
    $loginName = U @(0x0648, 0x0631, 0x0648, 0x062F)
    if (-not (Click-ByName $login $loginName)) { throw "login button not clicked" }
    Start-Sleep -Seconds 3

    # ---- 2) Main window: poll until a window bigger than the login card appears ----
    $main = $null
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $main) {
        Start-Sleep -Milliseconds 500
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $best = 0; $cand = $null
        foreach ($w in $wins) {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 600 -and $r.Height -gt 500 -and $r.Width * $r.Height -gt $best) { $best = $r.Width * $r.Height; $cand = $w }
        }
        if ($cand) { $main = $cand }
    }
    if (-not $main) {
        Write-Output "--- diagnostics: windows after login attempt ---"
        $wins2 = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins2) {
            $r = $w.Current.BoundingRectangle
            Write-Output ("WIN: {0}x{1} nameLen={2}" -f [int]$r.Width, [int]$r.Height, $w.Current.Name.Length)
            $eds = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
            foreach ($e in $eds) {
                try {
                    $v = ($e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
                    Write-Output ("  EDIT isPwd={0} len={1} value={2}" -f $e.Current.IsPassword, $v.Length, $v)
                } catch { Write-Output "  EDIT (no value pattern)" }
            }
        }
        throw "main window not found after login"
    }
    Write-Output "LOGIN OK"

    # ---- 3) open sell invoices page ----
    $menuName = U @(0xD83E, 0xDDFE, 0x0020, 0x0641, 0x0627, 0x06A9, 0x062A, 0x0648, 0x0631, 0x0647, 0x0627)
    $menu = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $menuName)))
    if (-not $menu) { throw "invoices menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()

    $subName = U @(0xD83E, 0xDDFE, 0x0020, 0x0641, 0x0627, 0x06A9, 0x062A, 0x0648, 0x0631, 0x0647, 0x0627, 0x06CC, 0x0020, 0x0641, 0x0631, 0x0648, 0x0634)
    $sub = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $sub) {
        Start-Sleep -Milliseconds 400
        $sub = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $subName)))
    }
    if (-not $sub) { throw "sell invoices menu item not found" }
    ($sub.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2

    # ---- 4) click new ----
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    $newBtnName = U @(0x2795, 0x0020, 0x062C, 0x062F, 0x06CC, 0x062F)
    if (-not (Click-ByName $main $newBtnName)) { throw "new button not clicked" }

    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) {
            if ($known -notcontains $w.H) { $dlgH = $w.H; break }
        }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "invoice editor window not found" }
    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgH)
    Write-Output "EDITOR OPEN"

    # editor loads catalog async — wait for save button to exist
    $saveName = U @(0xD83D, 0xDCBE, 0x0020, 0x0630, 0x062E, 0x06CC, 0x0631, 0x0647, 0x0020, 0x0641, 0x0627, 0x06A9, 0x062A, 0x0648, 0x0631)
    $saveBtn = $null
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and -not $saveBtn) {
        Start-Sleep -Milliseconds 400
        $saveBtn = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $saveName)))
    }
    if (-not $saveBtn) { throw "save button not found in editor" }
    Start-Sleep -Seconds 1   # let LoadDataAsync finish (number prefill)

    ($saveBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 3
    Write-Output "SAVE CLICKED"
}
catch {
    Write-Output "FAIL: $_"
}
finally {
    if ($script:appPid -ne 0) {
        Get-Process -Id $script:appPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
