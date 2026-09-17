param()
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace W -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
'@
[W.N]::SetProcessDPIAware() | Out-Null

$script:dumpList = New-Object System.Collections.Generic.List[object]
function Dump-Windows([uint32]$procId) {
    $script:dumpList.Clear()
    $cb = [W.N+EnumWindowsProc]{
        param($h, $l)
        [uint32]$wpid = 0
        [W.N]::GetWindowThreadProcessId($h, [ref]$wpid) | Out-Null
        if ($wpid -eq $procId) {
            $sb = New-Object System.Text.StringBuilder 256
            [W.N]::GetWindowText($h, $sb, 256) | Out-Null
            $script:dumpList.Add([pscustomobject]@{ H = $h; Title = $sb.ToString(); Visible = [W.N]::IsWindowVisible($h) })
        }
        return $true
    }
    [W.N]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:dumpList
}

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

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $wins = @(Dump-Windows $p.Id | Where-Object { $_.Visible -and $_.Title.Length -gt 5 })
    } while ($wins.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($wins.Count -eq 0) { throw "no main window" }
    $mainTitle = $wins[0].Title
    Write-Output "MAIN OK"
    Start-Sleep -Seconds 3

    $winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $main = $null
    foreach ($el in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $winCond)) {
        if ($el.Current.Name -eq $mainTitle) { $main = $el; break }
    }

    # open the attributes page directly from the menu
    $menu = Find-First $main ([System.Windows.Automation.ControlType]::MenuItem) '💼 پرونده‌ها'
    if (-not $menu) { throw "menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 800
    $item = Find-First $main ([System.Windows.Automation.ControlType]::MenuItem) '🏷️ ویژگی‌های کالا'
    if (-not $item) { throw "attributes menu item not found" }
    ($item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "ATTR PAGE OPENED (EF Attributes query OK)"

    # click ➕ جدید to open the editor (exercises FieldEditorWindow + layout)
    $btn = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not $btn -and [DateTime]::UtcNow -lt $deadline) {
        $btn = Find-First $main ([System.Windows.Automation.ControlType]::Button) '➕ جدید'
        if (-not $btn) { Start-Sleep -Milliseconds 400 }
    }
    if (-not $btn) { throw "new button not found" }
    ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "EDITOR OPENED"

    # find editor dialog by hwnd
    $dlgEl = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not $dlgEl -and [DateTime]::UtcNow -lt $deadline) {
        foreach ($w in (Dump-Windows $p.Id)) {
            if ($w.Visible -and $w.Title -eq 'ویژگی کالا') {
                $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($w.H); break
            }
        }
        if (-not $dlgEl) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $dlgEl) { throw "editor dialog not found" }
    Write-Output "DIALOG FOUND: ویژگی کالا"

    $editCond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $edits = $dlgEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
    $rects = @()
    foreach ($e2 in $edits) { $r = $e2.Current.BoundingRectangle; if ($r.Width -gt 0) { $rects += ,@([int]$r.X, [int]$r.Y) } }
    Write-Output ("EDIT FIELDS: {0}" -f $rects.Count)
    $i = 0
    foreach ($rr in $rects) { Write-Output ("  field{0}: x={1} y={2}" -f $i, $rr[0], $rr[1]); $i++ }

    $rect = $dlgEl.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $PSScriptRoot 'attrib-editor.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "PNG saved"
}
catch {
    Write-Output "ERROR: $($_.Exception.Message)"
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
