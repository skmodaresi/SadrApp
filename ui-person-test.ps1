param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Win32 -Name N5 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int n);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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

try {
    $app = Start-Process -FilePath $exe -PassThru
    $script:appPid = $app.Id
    Start-Sleep -Seconds 6

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
    if (-not (Click-ByName $login (U @(0x0648,0x0631,0x0648,0x062F)))) { throw "login click failed" }

    $main = $null
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not $main) {
        Start-Sleep -Milliseconds 500
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins) { $r = $w.Current.BoundingRectangle; if ($r.Width -gt 600 -and $r.Height -gt 500) { $main = $w; break } }
    }
    if (-not $main) { throw "main window not found" }
    Write-Output "LOGIN OK"

    # files menu: 💼 پرونده‌ها
    $menuName = U @(0xD83D, 0xDCBC, 0x0020, 0x067E, 0x0631, 0x0648, 0x0646, 0x062F, 0x0647, 0x200C, 0x0647, 0x0627)
    $menu = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $menuName)))
    if (-not $menu) { throw "files menu not found" }
    ($menu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()

    # 👥 اشخاص
    $subName = U @(0xD83D, 0xDC65, 0x0020, 0x0627, 0x0634, 0x062E, 0x0627, 0x0635)
    $sub = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline -and -not $sub) {
        Start-Sleep -Milliseconds 400
        $sub = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $subName)))
    }
    if (-not $sub) { throw "people menu item not found" }
    ($sub.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 2

    # new person dialog
    $known = @(Get-AppWindows | ForEach-Object { $_.H })
    if (-not (Click-ByName $main (U @(0x2795, 0x0020, 0x062C, 0x062F, 0x06CC, 0x062F)))) { throw "new click failed" }
    $dlgH = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $dlgH -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($w in (Get-AppWindows)) { if ($known -notcontains $w.H) { $dlgH = $w.H; break } }
    }
    if ($dlgH -eq [IntPtr]::Zero) { throw "person editor not found" }
    $dlg = [System.Windows.Automation.AutomationElement]::FromHandle($dlgH)
    Write-Output "PERSON EDITOR OPEN"

    # fill نام and نام خانوادگی (first and third plain textbox)
    $dEdits = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $plain = @()
    foreach ($e in $dEdits) { if (-not $e.Current.IsPassword) { $plain += $e } }
    if ($plain.Count -lt 3) { throw "person fields not found ($($plain.Count))" }
    ($plain[0].GetCurrentPattern($vp)).SetValue((U @(0x0639,0x0644,0x06CC)))            # علی
    ($plain[2].GetCurrentPattern($vp)).SetValue((U @(0x0631,0x0636,0x0627,0x06CC,0x06CC))) # رضایی
    Start-Sleep -Milliseconds 300

    # 💾 ذخیره
    if (-not (Click-ByName $dlg (U @(0xD83D, 0xDCBE, 0x0020, 0x0630, 0x062E, 0x06CC, 0x0631, 0x0647)))) { throw "save click failed" }
    Start-Sleep -Seconds 2
    Write-Output "PERSON SAVED"
}
catch {
    Write-Output "FAIL: $_"
}
finally {
    if ($script:appPid -ne 0) {
        Get-Process -Id $script:appPid -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
