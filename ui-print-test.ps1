param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Win32 -Name N5 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
public struct RECT { public int L; public int T; public int R; public int B; }
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
[Win32.N5]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$script:appPid = 0

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.N5+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.N5]::IsWindowVisible($h)) {
            $p = 0
            [Win32.N5]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
            if ($p -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.N5]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.N5]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
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

function Save-Shot($hwnd, [string]$path) {
    $r = New-Object Win32.N5+RECT
    [Win32.N5]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Select-ComboItem($container, [string]$item) {
    $combo = $container.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))
    if (-not $combo) { throw "combo not found" }
    ($combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    $target = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $target) {
        Start-Sleep -Milliseconds 300
        $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
        foreach ($it in $items) { if ($it.Current.Name -eq $item) { $target = $it; break } }
    }
    if (-not $target) {
        ($combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse()
        # diagnostics: dump the first few item names as char codes
        $items2 = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
        Write-Output ("COMBO ITEMS: {0}" -f $items2.Count)
        foreach ($it in $items2) {
            $codes = ($it.Current.Name.ToCharArray() | ForEach-Object { [int]$_ }) -join ','
            Write-Output ("ITEM[{0}]: {1}" -f $it.Current.Name.Length, $codes)
        }
        $allItems = $prev.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
        Write-Output ("ALL LISTITEMS UNDER PREVIEW: {0}" -f $allItems.Count)
        throw "combo item '$item' not found"
    }
    ($target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Seconds 2   # allow async re-render
}

try {
    $app = Start-Process -FilePath $exe -PassThru
    $script:appPid = $app.Id
    Start-Sleep -Seconds 6

    # ---- 1) Login ----
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
    $userEl = $null; $pwdEl = $null
    foreach ($e in $edits) { if ($e.Current.IsPassword) { $pwdEl = $e } else { $userEl = $e } }
    if (-not $userEl -or -not $pwdEl) { throw "login fields not identified" }
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    ($userEl.GetCurrentPattern($vp)).SetValue("admin")
    ($pwdEl.GetCurrentPattern($vp)).SetValue("admin")
    $loginName = U @(0x0648, 0x0631, 0x0648, 0x062F)
    if (-not (Click-ByName $login $loginName)) { throw "login button not clicked" }

    # ---- 2) Main window ----
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
    if (-not $main) { throw "main window not found after login" }
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

    # ---- 4) select first invoice row (DataGrid rows often have empty UIA names) ----
    $row = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $row) {
        Start-Sleep -Milliseconds 400
        $items = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
        foreach ($it in $items) {
            if ($it.Current.Name -like "*S-1405*") { $row = $it; break }
        }
        if (-not $row -and $items.Count -gt 0) { $row = $items[0]; break }
    }
    if (-not $row) { throw "no invoice row found in grid (dataItems=$($items.Count))" }
    ($row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 500
    Write-Output ("ROW SELECTED (name='{0}')" -f $row.Current.Name)

    # ---- 5) click the print button ----
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    $printBtnName = U @(0xD83D, 0xDDA8, 0x0020, 0x0686, 0x0627, 0x067E)
    if (-not (Click-ByName $main $printBtnName)) { throw "print button not clicked" }

    # ---- 6) wait for the preview window ----
    $prevH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and $prevH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        foreach ($w in (Get-AppWindows)) {
            if ($known -notcontains $w.H) { $prevH = $w.H; break }
        }
    }
    if ($prevH -eq [IntPtr]::Zero) { throw "preview window not found" }
    $prev = [System.Windows.Automation.AutomationElement]::FromHandle($prevH)
    Write-Output "PREVIEW OPEN"
    Start-Sleep -Seconds 1
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'ui-shots') | Out-Null
    Save-Shot $prevH (Join-Path $root 'ui-shots\layout-classic.png')

    # ---- 6b) switch through all 3 saved layouts ----
    $modern = U @(0x0633, 0x0627, 0x062F, 0x0647, 0x0020, 0x0648, 0x0020, 0x0645, 0x062F, 0x0631, 0x0646)
    $compact = U @(0x06A9, 0x0627, 0x0645, 0x067E, 0x06A9, 0x062A)
    Select-ComboItem $prev $modern
    Save-Shot $prevH (Join-Path $root 'ui-shots\layout-modern.png')
    Select-ComboItem $prev $compact
    Save-Shot $prevH (Join-Path $root 'ui-shots\layout-compact.png')
    Write-Output "LAYOUTS SWITCHED"

    # ---- 7) click save-PDF in the preview ----
    $savePdfName = U @(0xD83D, 0xDCBE, 0x0020, 0x0630, 0x062E, 0x06CC, 0x0631, 0x0647, 0x0020, 0x0050, 0x0044, 0x0046)
    if (-not (Click-ByName $prev $savePdfName)) { throw "save-pdf button not clicked" }

    # ---- 8) native SaveFileDialog: set filename, click Save ----
    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) {
            if ($w.H -ne $prevH -and $known -notcontains $w.H) { $dlgH = $w.H; break }
        }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "save dialog not found" }
    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgH)

    $pdfPath = Join-Path $root 'ui-test-invoice.pdf'
    if (Test-Path $pdfPath) { Remove-Item $pdfPath -Force }

    # UIA can't drive the Win32 file dialog (SetValue timeout) — activate it and
    # type the path into the focused filename box, then Enter = Save.
    [Win32.N5]::SetForegroundWindow($dlgH) | Out-Null
    Start-Sleep -Milliseconds 400
    if ([Win32.N5]::GetForegroundWindow() -ne $dlgH) {
        (New-Object -ComObject WScript.Shell).AppActivate($app.Id) | Out-Null
        Start-Sleep -Milliseconds 400
    }
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait($pdfPath + "{ENTER}")

    # ---- 9) verify the file ----
    $ok = $false; $size = 0
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $ok) {
        Start-Sleep -Milliseconds 500
        if (Test-Path $pdfPath) {
            $size = (Get-Item $pdfPath).Length
            if ($size -gt 1000) { $ok = $true }
        }
    }
    if (-not $ok) { throw "pdf file not written (size=$size)" }
    $head = [System.IO.File]::ReadAllBytes($pdfPath)[0..3]
    $magic = [System.Text.Encoding]::ASCII.GetString($head)
    Write-Output ("PDF SAVED: {0} bytes, header={1}" -f $size, $magic)
    if ($magic -ne "%PDF") { throw "not a PDF file" }

    # ---- 10) close preview ----
    Start-Sleep -Seconds 1
    Save-Shot $prevH (Join-Path $root 'ui-shots\print-preview-after-save.png')
    Write-Output "ALL OK"
}
catch {
    Write-Output "FAIL: $_"
}
finally {
    if ($script:appPid -ne 0) {
        Get-Process -Id $script:appPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
