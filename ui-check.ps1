[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Win32 -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int n);
'@
[Win32.Native]::SetProcessDPIAware() | Out-Null

$exe = Join-Path $PSScriptRoot 'SadrApp\bin\Debug\net10.0-windows\SadrApp.exe'
$p = Start-Process -FilePath $exe -PassThru
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

function Find-First($el, [System.Windows.Automation.ControlType]$type, [string]$name) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type)
    $c2 = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    $and = New-Object System.Windows.Automation.AndCondition($c1, $c2)
    $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
}

function Dump-Windows([uint32]$procId) {
    $script:dumpList = New-Object System.Collections.Generic.List[object]
    $cb = [Win32.Native+EnumWindowsProc]{
        param($h, $l)
        [uint32]$wpid = 0
        [Win32.Native]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
        if ($wpid -eq $procId) {
            $sb = New-Object System.Text.StringBuilder 256
            [Win32.Native]::GetWindowText($h, $sb, 256) | Out-Null
            $cn = New-Object System.Text.StringBuilder 256
            [Win32.Native]::GetClassName($h, $cn, 256) | Out-Null
            $vis = [Win32.Native]::IsWindowVisible($h)
            $script:dumpList.Add([pscustomobject]@{ H = $h; Title = $sb.ToString(); Class = $cn.ToString(); Visible = $vis })
        }
        return $true
    }
    [Win32.Native]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:dumpList
}

function Save-Shot([string]$path) {
    $w = [Win32.Native]::GetSystemMetrics(78)  # SM_CXVIRTUALSCREEN
    $h = [Win32.Native]::GetSystemMetrics(79)  # SM_CYVIRTUALSCREEN
    if ($w -le 0) { $w = [Win32.Native]::GetSystemMetrics(0); $h = [Win32.Native]::GetSystemMetrics(1) }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

try {
    # wait for main window
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $wins = @(Dump-Windows $p.Id | Where-Object { $_.Visible -and $_.Title.Length -gt 5 })
    } while ($wins.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($wins.Count -eq 0) { throw "no main window" }
    Write-Output "MAIN WIN: class=$($wins[0].Class) title=$($wins[0].Title)"
    Start-Sleep -Seconds 3

    # UIA on the visible main window
    $winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $main = $null
    foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $winCond)) {
        if ($el.Current.Name -eq $wins[0].Title) { $main = $el; break }
    }
    if (-not $main) { throw "UIA main not found" }

    $menu = Find-First $main ([System.Windows.Automation.ControlType]::MenuItem) '💼 پرونده‌ها'
    if (-not $menu) { throw "menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800
    $item = Find-First $main ([System.Windows.Automation.ControlType]::MenuItem) '📈 پروژه‌ها'
    if (-not $item) { throw "menu item not found" }
    ($item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "MENU OK"
    Start-Sleep -Seconds 2

    # wait for projects page button then invoke
    $btn = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not $btn -and [DateTime]::UtcNow -lt $deadline) {
        $btn = Find-First $main ([System.Windows.Automation.ControlType]::Button) '➕ پروژه جدید'
        if (-not $btn) { Start-Sleep -Milliseconds 400 }
    }
    if (-not $btn) { throw "button not found" }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "CLICK OK"

    # poll for dialog title via Win32; keep its HWND for direct UIA attach
    $dlgTitle = $null
    $dlgHwnd = [IntPtr]::Zero
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($w in (Dump-Windows $p.Id)) {
            if ($w.Visible -and $w.Title -like '*پروژه جدید*') { $dlgTitle = $w.Title; $dlgHwnd = $w.H; break }
        }
        if ($dlgTitle) { break }
        Start-Sleep -Milliseconds 600
    }

    Write-Output "WINDOWS AFTER CLICK:"
    foreach ($w in (Dump-Windows $p.Id)) {
        Write-Output ("  vis={0} class={1} title_len={2} title={3}" -f $w.Visible, $w.Class, $w.Title.Length, $w.Title)
    }
    Save-Shot (Join-Path $PSScriptRoot 'full-screen.png')
    Write-Output "FULL SHOT saved"

    if ($dlgTitle) {
        Write-Output "DIALOG FOUND: $dlgTitle"
        $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgHwnd)
        $types = @([System.Windows.Automation.ControlType]::Edit, [System.Windows.Automation.ControlType]::ComboBox, [System.Windows.Automation.ControlType]::CheckBox)
        $cond = New-Object System.Windows.Automation.OrCondition($types | ForEach-Object { New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $_) })
        $ctrls = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        $rows = @{}
        foreach ($c in $ctrls) {
            $r = $c.Current.BoundingRectangle
            if ($r.Width -le 0) { continue }
            $key = [int][Math]::Round($r.Y / 12)
            if (-not $rows.ContainsKey($key)) { $rows[$key] = @() }
            $rows[$key] += [pscustomobject]@{ T = $c.Current.ControlType.ProgrammaticName; X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width }
        }
        Write-Output "FIELD ROWS:"
        foreach ($k in ($rows.Keys | Sort-Object)) {
            $line = ($rows[$k] | ForEach-Object { "$($_.T.Split('::')[1]) x=$($_.X) y=$($_.Y) w=$($_.W)" }) -join ' || '
            Write-Output ("row{0}: {1}" -f $k, $line)
        }
        Write-Output ("DISTINCT ROWS: {0}" -f $rows.Count)

        # dialog screenshot
        $rect = $dlg.Current.BoundingRectangle
        $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $PSScriptRoot 'new-project-dialog.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        Write-Output "DIALOG SHOT saved"
    } else {
        Write-Output "DIALOG NEVER APPEARED"
    }
}
catch {
    Write-Output "ERROR: $($_.Exception.Message)"
    Save-Shot (Join-Path $PSScriptRoot 'error-screen.png')
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
