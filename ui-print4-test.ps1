param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -Namespace Win32 -Name N7 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
[DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder t, int n);
public struct RECT { public int L; public int T; public int R; public int B; }
public delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
'@
[Win32.N7]::SetProcessDPIAware() | Out-Null

$root = $PSScriptRoot
$exe  = Join-Path $root 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$script:appPid = 0

function U([int[]]$codes) { -join ($codes | ForEach-Object { [char]$_ }) }

function Get-AppWindows {
    $script:found = New-Object System.Collections.ArrayList
    $cb = [Win32.N7+EnumWindowsProc] {
        param($h, $lp)
        if ([Win32.N7]::IsWindowVisible($h)) {
            $p = 0
            [Win32.N7]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
            if ($p -eq $script:appPid) {
                $sb = New-Object System.Text.StringBuilder 256
                [Win32.N7]::GetWindowText($h, $sb, 256) | Out-Null
                if ($sb.Length -gt 0) { [void]$script:found.Add(@{ H = $h; T = $sb.ToString() }) }
            }
        }
        return $true
    }
    [Win32.N7]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Find-El($parent, [string]$name, $ctlType) {
    # UIA PropertyCondition matching is unreliable with surrogate-pair (emoji) names,
    # so filter by ControlType here and compare names in PowerShell.
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

function Save-Shot($hwnd, [string]$path) {
    $r = New-Object Win32.N7+RECT
    [Win32.N7]::GetWindowRect($hwnd, [ref]$r) | Out-Null
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
    if (-not $target) { ($combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Collapse(); throw "combo item not found" }
    ($target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Seconds 2
}

# Persian strings built from char codes (encoding-proof)
$setName   = U @(0x0686,0x0627,0x067E,0x0020,0x0633,0x0631,0x0628,0x0631,0x06AF,0x0020,0x062F,0x0627,0x0631)                    # چاپ سربرگ دار
$titleOver = U @(0x0641,0x0627,0x06A9,0x062A,0x0648,0x0631,0x0020,0x0641,0x0631,0x0648,0x0634,0x0020,0x0648,0x06CC,0x0698,0x0647)  # فاکتور فروش ویژه (U+0698 = ژ)
$footer    = U @(0x0628,0x0627,0x0020,0x062A,0x0634,0x06A9,0x0631,0x0020,0x0627,0x0632,0x0020,0x0647,0x0645,0x0631,0x0627,0x0647,0x06CC,0x0020,0x0634,0x0645,0x0627,0x0020,0x2014,0x0020,0x0635,0x062F,0x0631) # با تشکر از همراهی شما — صدر
$mnuSystem = U @(0x2699,0xFE0F,0x0020,0x0633,0x06CC,0x0633,0x062A,0x0645)                                                        # ⚙️ سیستم
$mnuPrint  = U @(0xD83D,0xDDA8,0x0020,0x062A,0x0646,0x0638,0x06CC,0x0645,0x0627,0x062A,0x0020,0x0686,0x0627,0x067E,0x0020,0x0641,0x0627,0x06A9,0x062A,0x0648,0x0631) # 🖨 تنظیمات چاپ فاکتور
$mnuInvoices = U @(0xD83E,0xDDFE,0x0020,0x0641,0x0627,0x06A9,0x062A,0x0648,0x0631,0x0647,0x0627)                                  # 🧾 فاکتورها
$mnuSell    = U @(0xD83E,0xDDFE,0x0020,0x0641,0x0627,0x06A9,0x062A,0x0648,0x0631,0x0647,0x0627,0x06CC,0x0020,0x0641,0x0631,0x0648,0x0634)  # 🧾 فاکتورهای فروش
$printBtn   = U @(0xD83D,0xDDA8,0x0020,0x0686,0x0627,0x067E)                                                                      # 🖨 چاپ
$savePdf   = U @(0xD83D,0xDCBE,0x0020,0x0630,0x062E,0x06CC,0x0631,0x0647,0x0020,0x0050,0x0044,0x0046)                            # 💾 ذخیره PDF
$loginBtn  = U @(0x0648,0x0631,0x0648,0x062F)                                                                                    # ورود

# ---------- 1) logo PNG ----------
$logoPath = Join-Path $root 'ui-shots\test-logo.png'
New-Item -ItemType Directory -Force -Path (Join-Path $root 'ui-shots') | Out-Null
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAlias'
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point(0,0)), (New-Object System.Drawing.Point(256,256)), [System.Drawing.Color]::FromArgb(31,58,95), [System.Drawing.Color]::FromArgb(47,111,237))
$g.FillRectangle($brush, 0, 0, 256, 256)
$f = New-Object System.Drawing.Font('Segoe UI', 130, [System.Drawing.FontStyle]::Bold)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
$g.DrawString('S', $f, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0,0,256,256)), $fmt)
$g.Dispose()
$bmp.Save($logoPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---------- 2) 4th setting row with logo ----------
$hex = ([System.IO.File]::ReadAllBytes($logoPath) | ForEach-Object { $_.ToString('X2') }) -join ''
$q = "IF EXISTS (SELECT 1 FROM InvoicePrintSettings WHERE Name = N'$setName') DELETE FROM InvoicePrintSettings WHERE Name = N'$setName'; " +
     "INSERT INTO InvoicePrintSettings (Name, Layout, IsDefault, TitleOverride, FooterNote, ShowDiscountColumn, Logo, Deleted, RecordUniqueId, CreateDateTime, UpdateDateTime) " +
     "VALUES (N'$setName', 1, 0, N'$titleOver', N'$footer', 0, 0x$hex, 0, NEWID(), GETDATE(), GETDATE());"
sqlcmd -S ".\SQLExpress" -d SadrTest -U sa -P "123asd!@#ASD" -Q $q | Out-Null
Write-Output "SETTING SEEDED"

$pdfPath = Join-Path $root 'ui-test-invoice4.pdf'
if (Test-Path $pdfPath) { Remove-Item $pdfPath -Force }

try {
    $app = Start-Process -FilePath $exe -PassThru
    $script:appPid = $app.Id
    Start-Sleep -Seconds 6

    # ---------- 3) login ----------
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
    $vp = [System.Windows.Automation.ValuePattern]::Pattern
    ($userEl.GetCurrentPattern($vp)).SetValue("admin")
    ($pwdEl.GetCurrentPattern($vp)).SetValue("admin")
    if (-not (Click-ByName $login $loginBtn)) { throw "login button not clicked" }

    # ---------- 4) main window ----------
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

    # ---------- 5) open sell invoices page ----------
    $menu = Find-El $main $mnuInvoices ([System.Windows.Automation.ControlType]::MenuItem)
    if (-not $menu) { throw "invoices menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    $sub = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $sub) {
        Start-Sleep -Milliseconds 400
        $subs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
        foreach ($s in $subs) { if ($s.Current.Name -eq $mnuSell) { $sub = $s; break } }
    }
    if (-not $sub) { throw "sell invoices menu item not found" }
    ($sub.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2

    # ---------- 6) open print settings window ----------
    $menu = Find-El $main $mnuSystem ([System.Windows.Automation.ControlType]::MenuItem)
    if (-not $menu) { throw "system menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800
    $item = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $item) {
        Start-Sleep -Milliseconds 400
        $subs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
        foreach ($s in $subs) { if ($s.Current.Name -eq $mnuPrint) { $item = $s; break } }
    }
    if (-not $item) { throw "print settings menu item not found" }
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    ($item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()

    $setH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and $setH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($known -notcontains $w.H) { $setH = $w.H; break } }
    }
    if ($setH -eq [IntPtr]::Zero) {
        Write-Output "SETTINGS WINDOW NOT FOUND - app alive: $(-not $app.HasExited)"
        Write-Output "WINDOW DUMP:"
        foreach ($w in (Get-AppWindows)) { Write-Output ("  hwnd={0} class={1} title={2}" -f $w.H, $w.C, $w.T) }
        foreach ($w in (Get-AppWindows)) { Save-Shot $w.H (Join-Path $root ("ui-shots\fail-" + $w.H + ".png")) }
        throw "settings window not found"
    }
    $setEl = [System.Windows.Automation.AutomationElement]::FromHandle($setH)
    $rows = $setEl.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
    Write-Output ("SETTINGS OPEN, GRID ROWS: {0}" -f $rows.Count)
    Save-Shot $setH (Join-Path $root 'ui-shots\print-settings-window.png')
    [Win32.N7]::SendMessage($setH, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_CLOSE
    Start-Sleep -Seconds 1

    # ---------- 7) re-find main, select invoice row, print ----------
    $main = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $main) {
        Start-Sleep -Milliseconds 400
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        $best = 0; $cand = $null
        foreach ($w in $wins) {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 600 -and $r.Height -gt 500 -and ($r.Width * $r.Height) -gt $best) { $best = $r.Width * $r.Height; $cand = $w }
        }
        if ($cand) { $main = $cand }
    }
    if (-not $main) { throw "main window lost" }

    $row = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $row) {
        Start-Sleep -Milliseconds 400
        $items = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)))
        foreach ($it in $items) { if ($it.Current.Name -like "*S-1405*") { $row = $it; break } }
        if (-not $row -and $items.Count -gt 0) { $row = $items[0]; break }
    }
    if (-not $row) { throw "no invoice row found" }
    ($row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 500

    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    if (-not (Click-ByName $main $printBtn)) { throw "print button not clicked" }

    $prevH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and $prevH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        foreach ($w in (Get-AppWindows)) { if ($known -notcontains $w.H) { $prevH = $w.H; break } }
    }
    if ($prevH -eq [IntPtr]::Zero) { throw "preview window not found" }
    $prev = [System.Windows.Automation.AutomationElement]::FromHandle($prevH)
    Write-Output "PREVIEW OPEN"
    Start-Sleep -Seconds 1

    # ---------- 8) switch to the logo layout ----------
    Select-ComboItem $prev $setName
    Save-Shot $prevH (Join-Path $root 'ui-shots\layout-logo.png')
    Write-Output "LOGO LAYOUT SELECTED"

    # ---------- 9) save PDF ----------
    if (-not (Click-ByName $prev $savePdf)) { throw "save-pdf button not clicked" }
    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($w.H -ne $prevH -and $known -notcontains $w.H) { $dlgH = $w.H; break } }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "save dialog not found" }

    # Drive the Win32 dialog without focus: WM_SETTEXT on its filename Edit, then IDOK.
    $script:edits = New-Object System.Collections.ArrayList
    $cb = [Win32.N7+EnumWindowsProc] {
        param($h, $lp)
        $cn = New-Object System.Text.StringBuilder 128
        [Win32.N7]::GetClassName($h, $cn, 128) | Out-Null
        if ($cn.ToString() -eq 'Edit') { [void]$script:edits.Add($h) }
        return $true
    }
    [Win32.N7]::EnumChildWindows($dlgH, $cb, [IntPtr]::Zero) | Out-Null
    if ($script:edits.Count -eq 0) { throw "filename edit not found in dialog" }
    [Win32.N7]::SendMessage([IntPtr]$script:edits[0], 0x000C, [IntPtr]::Zero, $pdfPath) | Out-Null   # WM_SETTEXT
    Start-Sleep -Milliseconds 300
    [Win32.N7]::SendMessage($dlgH, 0x0111, [IntPtr]1, [IntPtr]::Zero) | Out-Null   # WM_COMMAND, IDOK=1

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
    $raw = [System.IO.File]::ReadAllBytes($pdfPath)
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString($raw)   # latin-1 byte view
    $imgCount = ([regex]::Matches($text, "/Subtype\s*/Image")).Count
    $size = $raw.Length
    Write-Output ("PDF SAVED: {0} bytes, header={1}, raster-images={2}" -f $size, $magic, $imgCount)
    if ($magic -ne "%PDF") { throw "not a PDF file" }
    if ($imgCount -lt 1) { throw "logo image missing from PDF" }
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
