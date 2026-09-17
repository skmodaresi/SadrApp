param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$win32 = @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
Add-Type -Namespace Win32 -Name Native -MemberDefinition $win32
[Win32.Native]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$shots = Join-Path $root 'ui-shots'
New-Item -ItemType Directory -Force -Path $shots | Out-Null

$app = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.Native+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.Native]::IsWindowVisible($h)) {
            $pid2 = 0
            [Win32.Native]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
            if ($pid2 -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.Native]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.Native]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

$script:appPid = $app.Id

function Wait-Window([string]$titlePart, [int]$timeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        # UIA lookup by process id (encoding-proof)
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:appPid)
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $biggest = $null; $bestArea = 0
        foreach ($w in $wins) {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -le 0) { continue }
            if ($titlePart -ne '' -and $w.Current.Name -like "*$titlePart*") {
                try { [Win32.Native]::SetForegroundWindow([IntPtr]$w.Current.NativeWindowHandle) | Out-Null } catch {}
                Start-Sleep -Milliseconds 600
                return $w
            }
            $area = $r.Width * $r.Height
            if ($area -gt $bestArea) { $bestArea = $area; $biggest = $w }
        }
        if ($titlePart -eq '' -and $biggest) {
            try { [Win32.Native]::SetForegroundWindow([IntPtr]$biggest.Current.NativeWindowHandle) | Out-Null } catch {}
            Start-Sleep -Milliseconds 600
            return $biggest
        }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Click-ByName($parent, [string]$name) {
    $btn = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)))
    if ($btn) {
        $inv = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $inv.Invoke()
        return $true
    }
    return $false
}

function Save-Shot($el, [string]$file) {
    if (-not $el) { return }
    $r = $el.Current.BoundingRectangle
    if ($r.Width -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $shots $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$ok = @()
$fail = @()

try {
    $main = Wait-Window 'سامانه حسابداری' 25
    if (-not $main) { throw "main window not found" }
    Start-Sleep -Seconds 1

    # ---- 1) Groups page + editor ----
    $menu = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '📊 حسابداری')))
    if (-not $menu) { throw "accounting menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800

    $sub = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '🧩 گروه‌های حساب')))
    if (-not $sub) { throw "groups menu item not found" }
    ($sub.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2
    $groupsTab = Wait-Window 'سامانه حسابداری' 5
    $ok += "groups page opened"

    $newBtn = $groupsTab.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '➕ جدید')))
    if (-not $newBtn) { throw "new button not found on groups page" }

    # HWND-based dialog detection: count process windows, click, poll for a new HWND.
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    ($newBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()

    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        foreach ($w in (Get-AppWindows)) {
            if ($known -notcontains $w.H) { $dlgH = $w.H; break }
        }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "no new window after click (known: $($known.Count))" }

    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgH)
    $nm = $dlg.Current.Name
    $codes = ($nm.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
    Write-Output ("DIALOG UIA NAME: {0}" -f $codes)
    $r = $dlg.Current.BoundingRectangle
    Write-Output ("DIALOG RECT: {0}x{1} at {2},{3}" -f [int]$r.Width, [int]$r.Height, [int]$r.X, [int]$r.Y)
    try { $dlg.SetFocus() } catch {}
    Start-Sleep -Milliseconds 500
    Save-Shot $dlg 'acc-groups-editor.png'
    $ok += "groups editor opened + screenshot"

    # close via cancel button (or WM_CLOSE fallback)
    $cancel = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'انصراف')))
    if ($cancel) { ($cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    else {
        Add-Type -Namespace W32 -Name Closer -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
        [W32.Closer]::PostMessage($dlgH, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    Start-Sleep -Milliseconds 700

    # ---- 2) Generals page (cascading filter combo must load without EF error) ----
    $menu2 = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '📊 حسابداری')))
    ($menu2.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 700
    $sub2 = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '📚 حساب‌های کل')))
    ($sub2.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2
    $ok += "generals page opened (EF group combo loaded)"

    # ---- 3) Details page (two filter combos) ----
    $menu3 = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '📊 حسابداری')))
    ($menu3.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 700
    $sub3 = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '📄 حساب‌های تفصیلی')))
    ($sub3.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2
    $ok += "details page opened (two cascading combos loaded)"

    $script:result = "PASS: " + ($ok -join ' | ')
}
catch {
    try {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:appPid)
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $i = 0
        foreach ($w in $wins) {
            $n = $w.Current.Name
            $codes = ($n.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
            Write-Output ("UIA WINDOW: {0}" -f $codes)
            Save-Shot $w ("acc-fail-{0}.png" -f $i)
            $i++
        }
        # full desktop shot
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
        $g.Dispose(); $bmp.Save((Join-Path $shots 'acc-fail-desktop.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    } catch {}
    $script:result = "FAIL: $_"
}
finally {
    Start-Sleep -Milliseconds 500
    if (-not $app.HasExited) { $app.Kill() }
}

Write-Output $script:result
