# UIA smoke test for task management:
# login -> open tasks page -> new task -> fill editor -> save -> reports window -> new report -> save
# DB rows verified afterwards by sqlcmd in the terminal.
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Win32 -Name N8 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
public struct RECT { public int L; public int T; public int R; public int B; }
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
[Win32.N8]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$script:appPid = 0

function U([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.N8+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.N8]::IsWindowVisible($h)) {
            $p = 0
            [Win32.N8]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
            if ($p -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.N8]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.N8]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Find-El($parent, [string]$name, $ctlType) {
    $el = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $el) {
        Start-Sleep -Milliseconds 400
        $els = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ctlType)))
        foreach ($e in $els) { if ($e.Current.Name -eq $name) { $el = $e; break } }
    }
    return $el
}

function Click-ByName($parent, [string]$name) {
    $btn = Find-El $parent $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $btn) { return $false }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    return $true
}

function Get-SortedEdits($el) {
    # Edits sorted visually: topmost row first; within a row RTL = larger X first.
    $eds = @($el.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit))))
    return ($eds | Sort-Object { $_.Current.BoundingRectangle.Y }, { -($_.Current.BoundingRectangle.X) })
}

function Save-Shot($hwnd, [string]$path) {
    $r = New-Object Win32.N8+RECT
    [Win32.N8]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Persian strings (encoding-proof)
$mnuFiles = U @(0xD83D,0xDCBC,0x0020,0x067E,0x0631,0x0648,0x0646,0x062F,0x0647,0x200C,0x0647,0x0627)   # 💼 پرونده‌ها (ZWNJ U+200C)
$mnuTasks = U @(0xD83D,0xDCCB,0x0020,0x0648,0x0638,0x0627,0x06CC,0x0641,0x0020,0x067E,0x0631,0x0648,0x0698,0x0647,0x200C,0x0647,0x0627) # 📋 وظایف پروژه‌ها
$newTaskBtn  = U @(0x2795,0x0020,0x062C,0x062F,0x06CC,0x062F)                                          # ➕ جدید
$reportsBtn  = U @(0xD83D,0xDCCB,0x0020,0x06AF,0x0632,0x0627,0x0631,0x0634,0x200C,0x0647,0x0627,0x06CC,0x0020,0x0648,0x0638,0x06CC,0x0641,0x0647) # 📋 گزارش‌های وظیفه
$taskEdTitle = U @(0x0648,0x0638,0x06CC,0x0641,0x0647,0x0020,0x062C,0x062F,0x06CC,0x062F)                # وظیفه جدید
$newReportBtn = U @(0x2795,0x0020,0x06AF,0x0632,0x0627,0x0631,0x0634,0x0020,0x062C,0x062F,0x06CC,0x062F) # ➕ گزارش جدید
$saveBtn  = U @(0xD83D,0xDCBE,0x0020,0x0630,0x062E,0x06CC,0x0631,0x0647)                               # 💾 ذخیره
$loginBtn = U @(0x0648,0x0631,0x0648,0x062F)                                                          # ورود
$taskName = U @(0x062A,0x062D,0x0648,0x06CC,0x0631,0x0020,0x062A,0x0633,0x062A,0x0020,0x0627,0x0648,0x0644)  # تحویل تست اول
$reportText = U @(0x06AF,0x0632,0x0627,0x0631,0x0634,0x0020,0x0622,0x0632,0x0645,0x0627,0x06CC,0x0634,0x06CC) # گزارش آزمایشی

# ---------- 1) launch ----------
$p = Start-Process -FilePath $exe -PassThru
$script:appPid = $p.Id
try {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)

    # ---------- 2) login ----------
    $login = $null
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $login) {
        Start-Sleep -Milliseconds 500
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins) { if ($w.Current.BoundingRectangle.Width -gt 0) { $login = $w; break } }
    }
    if (-not $login) { throw "login window not found" }
    $edits = $login.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $userEl = $null; $pwdEl = $null
    foreach ($e in $edits) { if ($e.Current.IsPassword) { $pwdEl = $e } else { $userEl = $e } }
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    ($userEl.GetCurrentPattern($vp)).SetValue("admin")
    ($pwdEl.GetCurrentPattern($vp)).SetValue("admin")
    if (-not (Click-ByName $login $loginBtn)) { throw "login button not clicked" }

    # ---------- 3) main window ----------
    $main = $null
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $main) {
        Start-Sleep -Milliseconds 500
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $best = 0; $cand = $null
        foreach ($w in $wins) {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 600 -and $r.Height -gt 500 -and ($r.Width * $r.Height) -gt $best) { $best = $r.Width * $r.Height; $cand = $w }
        }
        if ($cand) { $main = $cand }
    }
    if (-not $main) { throw "main window not found" }
    Write-Output "LOGIN OK"

    # ---------- 4) open tasks page ----------
    $menu = Find-El $main $mnuFiles ([System.Windows.Automation.ControlType]::MenuItem)
    if (-not $menu) { throw "files menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800
    $item = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $item) {
        Start-Sleep -Milliseconds 400
        $subs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
        foreach ($s in $subs) { if ($s.Current.Name -eq $mnuTasks) { $item = $s; break } }
    }
    if (-not $item) { throw "tasks menu item not found" }
    ($item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2
    Write-Output "TASKS PAGE OPEN"

    # ---------- 5) new task ----------
    if (-not (Click-ByName $main $newTaskBtn)) { throw "new task button not clicked" }

    # editor window = the one titled وظیفه جدید
    $edH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $edH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($w.T -eq $taskEdTitle) { $edH = $w.H; break } }
    }
    if ($edH -eq [IntPtr]::Zero) { throw "task editor window not found" }
    $ed = [System.Windows.Automation.AutomationElement]::FromHandle($edH)
    Write-Output "TASK EDITOR OPEN"
    Save-Shot $edH (Join-Path $root 'ui-shots\task-editor.png')

    # project combo = the topmost combo (row 0, right pair); task name = topmost Edit in the same row
    $combos = @($ed.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox))))
    $projCombo = $combos | Sort-Object { $_.Current.BoundingRectangle.Y } | Select-Object -First 1
    $ec = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
    ($projCombo.GetCurrentPattern($ec)).Expand()
    $target = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $target) {
        Start-Sleep -Milliseconds 300
        $items = $projCombo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
        foreach ($it in $items) { if ($it.Current.Name.Length -gt 2) { $target = $it; break } }  # first real project
    }
    if (-not $target) { ($projCombo.GetCurrentPattern($ec)).Collapse(); throw "project combo item not found" }
    ($target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 500

    $tb = Get-SortedEdits $ed
    if ($tb.Count -lt 3) { throw "editor edits not found: $($tb.Count)" }
    ($tb[0].GetCurrentPattern($vp)).SetValue($taskName)
    Start-Sleep -Milliseconds 300
    # responsible person combo = 2nd combo (visual row 2, left pair)
    $combos2 = $combos | Sort-Object { $_.Current.BoundingRectangle.Y }
    if ($combos2.Count -ge 2) {
        $respCombo = $combos2[1]
        ($respCombo.GetCurrentPattern($ec)).Expand()
        $rt = $null
        $deadline = (Get-Date).AddSeconds(8)
        while ((Get-Date) -lt $deadline -and -not $rt) {
            Start-Sleep -Milliseconds 300
            $items = $respCombo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
            foreach ($it in $items) { if ($it.Current.Name.Length -gt 2) { $rt = $it; break } }
        }
        if ($rt) { ($rt.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select(); Start-Sleep -Milliseconds 400 }
        else { ($respCombo.GetCurrentPattern($ec)).Collapse() }
    }
    Save-Shot $edH (Join-Path $root 'ui-shots\task-editor-filled.png')
    Write-Output "EDITOR FILLED"

    if (-not (Click-ByName $ed $saveBtn)) { throw "save button not clicked" }
    # wait for editor to close (or a validation message box to appear)
    $closed = $false
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $stillOpen = $false
        foreach ($w in (Get-AppWindows)) { if ($w.H -eq $edH) { $stillOpen = $true } }
        if (-not $stillOpen) { $closed = $true; break }
    }
    if (-not $closed) {
        Write-Output "EDITOR STILL OPEN AFTER SAVE -- dumping state:"
        foreach ($w in (Get-AppWindows)) { Write-Output ("  WINDOW: [" + $w.T + "]") }
        Save-Shot $edH (Join-Path $root 'ui-shots\task-editor-after-save.png')
        throw "editor did not close after save"
    }
    Start-Sleep -Seconds 1
    foreach ($w in (Get-AppWindows)) {
        $hexT = ([System.Text.Encoding]::Unicode.GetBytes($w.T) | ForEach-Object { $_.ToString('X2') }) -join ' '
        Write-Output ("WINDOW AFTER EDITOR CLOSED: [" + $w.T + "] utf16: " + $hexT)
        Save-Shot $w.H (Join-Path $root ("ui-shots\win-" + $w.H + ".png"))
    }
    Write-Output "TASK SAVED"

    # ---------- 6) select the row, open reports ----------
    $row = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $row) {
        Start-Sleep -Milliseconds 400
        $items = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
        foreach ($it in $items) { if (-not $row) { $row = $it } }
        if ($row) { break }
    }
    if (-not $row) { throw "no task row found" }
    ($row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 500

    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    if (-not (Click-ByName $main $reportsBtn)) { throw "reports button not clicked" }
    $repH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and $repH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($known -notcontains $w.H) { $repH = $w.H; break } }
    }
    if ($repH -eq [IntPtr]::Zero) { throw "reports window not found" }
    $rep = [System.Windows.Automation.AutomationElement]::FromHandle($repH)
    Write-Output "REPORTS WINDOW OPEN"
    Save-Shot $repH (Join-Path $root 'ui-shots\task-reports.png')

    # ---------- 7) new report ----------
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    if (-not (Click-ByName $rep $newReportBtn)) { throw "new report button not clicked" }
    $redH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $redH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($known -notcontains $w.H) { $redH = $w.H; break } }
    }
    if ($redH -eq [IntPtr]::Zero) { throw "report editor window not found" }
    $red = [System.Windows.Automation.AutomationElement]::FromHandle($redH)

    $redEdits = Get-SortedEdits $red
    if ($redEdits.Count -lt 3) { throw "report editor fields not found: $($redEdits.Count)" }
    # visual rows: [0]=date picker text, [1]=cost, [2]=description (multiline)
    ($redEdits[1].GetCurrentPattern($vp)).SetValue("250000")
    ($redEdits[2].GetCurrentPattern($vp)).SetValue($reportText)
    if (-not (Click-ByName $red $saveBtn)) { throw "report save not clicked" }
    Start-Sleep -Seconds 2
    Write-Output "REPORT SAVED"
    Save-Shot $repH (Join-Path $root 'ui-shots\task-reports-after.png')

    Write-Output "ALL OK"
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
