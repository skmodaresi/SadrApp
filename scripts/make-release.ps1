# Creates (or reuses) a GitHub release for this repo and uploads a zip asset.
# Usage: powershell -File scripts/make-release.ps1 -Zip <path> -Tag v1.0.0 [-Title "..."]
# Auth: reuses the credential Git Credential Manager stored for github.com
#       (token is read via `git credential fill`, never printed).
param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Title
)
$ErrorActionPreference = 'Stop'
$repo = 'skmodaresi/SadrApp'

if (-not (Test-Path $Zip)) { throw "zip not found: $Zip" }
if (-not $Title) { $Title = $Tag }

# --- token from Git Credential Manager (never printed, never stored) ---
$credText = "protocol=https`nhost=github.com`n`n" | & git credential fill
$token = ($credText | Select-String '^password=').Line.Substring('password='.Length)
if (-not $token) { throw "no stored GitHub credential found - run a git push once first" }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }

$notes = @'
Offline installer for testers.

**Install:** unzip, right-click `SadrSetup.exe` -> *Run as administrator*.
The wizard installs the .NET 10 runtime and SQL Server 2022 LocalDB if they
are missing, creates and starts the LocalDB instance, creates the database,
and adds a Start-Menu shortcut. On first launch the app asks for the main
admin username/password - no default credentials exist.

Requires Windows 10/11 x64. Microsoft Visual C++ 2015-2022 Redistributable
(x64) must be present (it usually already is; LocalDB needs it).
'@

function Invoke-ApiRetry {
    param($Method, $Uri, $Body = $null, $TimeoutSec = 120)
    for ($i = 1; $i -le 4; $i++) {
        try {
            $p = @{ Method = $Method; Uri = $Uri; Headers = $script:headers; TimeoutSec = $TimeoutSec }
            if ($null -ne $Body) { $p.Body = $Body; $p.ContentType = 'application/json' }
            return Invoke-RestMethod @p
        } catch {
            Write-Host "  attempt $i failed: $($_.Exception.Message)"
            Start-Sleep -Seconds 3
        }
    }
    throw "API failed after retries: $Method $Uri"
}

Write-Host "== checking release $Tag =="
$rel = $null
try { $rel = Invoke-ApiRetry GET "https://api.github.com/repos/$repo/releases/tags/$Tag" } catch { }
if (-not $rel -or -not $rel.id) {
    Write-Host "== creating release $Tag =="
    # Encode as UTF-8 bytes: PS 5.1 otherwise sends the body in the legacy ANSI
    # codepage and destroys non-ASCII titles (they arrive as literal '?').
    $json = [Text.Encoding]::UTF8.GetBytes((@{ tag_name = $Tag; name = $Title; body = $notes } | ConvertTo-Json))
    $rel = Invoke-ApiRetry POST "https://api.github.com/repos/$repo/releases" $json
} else {
    Write-Host "release $Tag already exists (id $($rel.id)) - uploading asset to it"
}

$assetName = [IO.Path]::GetFileName($Zip)

# Re-run support: drop an old asset with the same name before uploading.
foreach ($a in @($rel.assets)) {
    if ($a.name -eq $assetName) {
        Write-Host "deleting stale asset '$assetName'..."
        Invoke-ApiRetry DELETE "https://api.github.com/repos/$repo/releases/assets/$($a.id)" | Out-Null
    }
}

Write-Host "== uploading $assetName (this may take a while) =="
$up = $null
for ($i = 1; $i -le 3; $i++) {
    try {
        $up = Invoke-RestMethod -Method Post -Headers $headers -ContentType 'application/zip' `
            -InFile $Zip -TimeoutSec 570 `
            -Uri "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$assetName"
        break
    } catch {
        Write-Host "  upload attempt $i failed: $($_.Exception.Message)"
        Start-Sleep -Seconds 3
    }
}
if (-not $up -or $up.state -ne 'uploaded') { throw "asset upload failed" }

Write-Host "== done =="
Write-Host "RELEASE: $($rel.html_url)"
