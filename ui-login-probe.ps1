# UIA probe: types a test password into the login window's masked PasswordBox,
# toggles the reveal checkbox, and reads the stored value via the reveal TextBox.
# Matches elements by ProcessId + AutomationId only (no localized-name lookups).
param([int]$AppPid = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

if ($AppPid -eq 0) {
    $AppPid = (Get-Process SadrApp | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).Id
}
Write-Output ("target pid: " + $AppPid)

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppPid)))
if (-not $win) { Write-Output 'RESULT: LOGIN WINDOW NOT FOUND'; exit 1 }
Write-Output ("found window: class=" + $win.Current.ClassName)

function Find-Aid($el, $aid) {
    return $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $aid)))
}

$pwdBox = Find-Aid $win 'TxtPassword'
if (-not $pwdBox) { Write-Output 'RESULT: TXT_PASSWORD NOT FOUND'; exit 1 }

$pwdBox.SetFocus()
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('admin123')
Start-Sleep -Milliseconds 500
Write-Output 'typed admin123 into masked box'

$chk = Find-Aid $win 'ChkShow'
if (-not $chk) { Write-Output 'RESULT: CHECKBOX NOT FOUND'; exit 1 }
$tog = $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($tog.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) { $tog.Toggle() }
Start-Sleep -Milliseconds 400
Write-Output 'reveal checkbox toggled ON'

$reveal = Find-Aid $win 'TxtReveal'
if (-not $reveal) { Write-Output 'RESULT: TXT_REVEAL NOT FOUND'; exit 1 }
$stored = ($reveal.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
Write-Output ("stored value read back: '" + $stored + "'")

if ($stored -eq 'admin123') { Write-Output 'RESULT: STORED-OK (matches typed text)' }
elseif ($stored -eq '321nimda') { Write-Output 'RESULT: STORED-REVERSED (masked box stores reversed)' }
else { Write-Output ("RESULT: STORED-OTHER '" + $stored + "'") }

# cleanup: toggle reveal back off and clear fields
if ($tog.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) { $tog.Toggle() }
Start-Sleep -Milliseconds 300
foreach ($aid in 'TxtUsername','TxtPassword','TxtReveal') {
    $el = Find-Aid $win $aid
    if ($el) { try { ($el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue('') } catch {} }
}
Write-Output 'cleanup done'
