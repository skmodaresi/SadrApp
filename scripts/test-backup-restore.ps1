# Self-test for the exact T-SQL sequence BackupWindow runs (SadrApp).
# Simulates the full lifecycle on a scratch database, never on the real SadrTest database:
#   backup -> restore-with-MOVE into a scratch db -> in-db single-user restore -> reconnect -> cleanup
# Usage: powershell -ExecutionPolicy Bypass -File scripts/test-backup-restore.ps1 [-Server .\SQLExpress] [-Db SadrTest]
param(
    [string]$Server = ".\SQLExpress",
    [string]$Db = "SadrTest"
)

$ErrorActionPreference = "Stop"
$master = "Data Source=$Server;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True"
$con = New-Object System.Data.SqlClient.SqlConnection($master)
$con.Open()

function Exec([System.Data.SqlClient.SqlConnection]$c, [string]$sql) {
    $cmd = $c.CreateCommand()
    $cmd.CommandTimeout = 300
    $cmd.CommandText = $sql
    $cmd.ExecuteNonQuery() | Out-Null
}

function Rows([System.Data.SqlClient.SqlConnection]$c, [string]$sql) {
    # Returns a DataTable - stable indexing, same pattern as the app's Database.Query.
    $cmd = $c.CreateCommand()
    $cmd.CommandTimeout = 300
    $cmd.CommandText = $sql
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($cmd)
    $dt = New-Object System.Data.DataTable
    [void]$da.Fill($dt)
    return ,$dt  # comma prevents PowerShell from unrolling the DataTable into DataRows
}

try {
    # Server data dir (RESTORE MOVE destinations) and a staging dir the SQL service can
    # write into and the app user can read/move/delete (same strategy as BackupWindow).
    $dt = Rows $con "SELECT TOP 1 LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1) AS d FROM sys.master_files WHERE database_id = 2 AND type = 0"
    $dataDir = $dt.Rows[0]["d"].ToString()
    $stage = [Environment]::GetFolderPath("CommonDocuments")
    Write-Host ("Server data dir: " + $dataDir)
    Write-Host ("Staging dir:     " + $stage)

    $scratch = "SadrBackupSelfTest"

    # 1) Backup the real database (same statement shape as BackupWindow).
    $testBak = Join-Path $stage ($scratch + "_src.bak")
    if (Test-Path $testBak) { Remove-Item $testBak -Force }
    try {
        Exec $con "BACKUP DATABASE [$Db] TO DISK = '$testBak' WITH INIT, COMPRESSION"
    }
    catch {
        # Express Edition rejects COMPRESSION - same fallback BackupWindow implements.
        Exec $con "BACKUP DATABASE [$Db] TO DISK = '$testBak' WITH INIT"
    }
    Write-Host ("1. BACKUP ok: " + [math]::Round((Get-Item $testBak).Length / 1KB) + " KB")

    # 2) Restore using FILELISTONLY + MOVE (BackupWindow's fallback path) into a scratch db.
    $f = Rows $con "RESTORE FILELISTONLY FROM DISK = '$testBak'"
    $moves = @()
    foreach ($row in $f.Rows) {
        $logical = $row["LogicalName"].ToString().Replace("'", "''")
        $type = $row["Type"].ToString()
        $ext = if ($type -eq "L") { "_log.ldf" } else { ".mdf" }
        $dest = Join-Path $dataDir ($scratch + $ext)  # service-writable; no user pre-check possible/needed
        $moves += ("MOVE N'" + $logical + "' TO N'" + $dest.Replace("'", "''") + "'")
    }
    Exec $con ("RESTORE DATABASE [$scratch] FROM DISK = '$testBak' WITH REPLACE, RECOVERY, " + ($moves -join ", "))
    Write-Host "2. RESTORE with MOVE into scratch db ok"

    # 3) Backup the scratch db (the file we will restore over itself, in-db).
    $simBak = Join-Path $stage ($scratch + "_self.bak")
    if (Test-Path $simBak) { Remove-Item $simBak -Force }
    try {
        Exec $con "BACKUP DATABASE [$scratch] TO DISK = '$simBak' WITH INIT, COMPRESSION"
    }
    catch {
        Exec $con "BACKUP DATABASE [$scratch] TO DISK = '$simBak' WITH INIT"
    }
    Write-Host ("3. BACKUP of scratch ok: " + [math]::Round((Get-Item $simBak).Length / 1KB) + " KB")

    # 4) The exact sequence BackupWindow now uses: RESTORE must run from a master-catalog
    #    connection while the app's own connection sits inside the target db.
    $appCs = "Data Source=$Server;Initial Catalog=$scratch;Integrated Security=True;TrustServerCertificate=True"
    $c2 = New-Object System.Data.SqlClient.SqlConnection($appCs)
    $c2.Open()  # simulates the app connection living INSIDE the db
    Exec $con "ALTER DATABASE [$scratch] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
    Write-Host "4a. SET SINGLE_USER from master context ok (app connection killed)"

    Exec $con "RESTORE DATABASE [$scratch] FROM DISK = '$simBak' WITH REPLACE, RECOVERY"
    Write-Host "4b. RESTORE DATABASE from master context ok"

    Exec $con "ALTER DATABASE [$scratch] SET MULTI_USER"
    [System.Data.SqlClient.SqlConnection]::ClearAllPools()
    $c3 = New-Object System.Data.SqlClient.SqlConnection($appCs)
    $c3.Open()
    $n = (Rows $c3 "SELECT COUNT(*) AS c FROM sys.tables").Rows[0]["c"]
    Write-Host ("4c. app reconnected after restore; scratch db has " + $n + " tables")
    $c3.Close()
    $c2.Close()

    # 5) Cleanup scratch db and files (clear pooled connections, force single-user, drop).
    [System.Data.SqlClient.SqlConnection]::ClearAllPools()
    Exec $con "IF DB_ID('$scratch') IS NOT NULL ALTER DATABASE [$scratch] SET SINGLE_USER WITH ROLLBACK IMMEDIATE"
    Exec $con "IF DB_ID('$scratch') IS NOT NULL DROP DATABASE [$scratch]"
    Remove-Item $testBak, $simBak -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $stage -Filter ($scratch + "*") -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host "5. cleanup ok"
    Write-Host "ALL BACKUP/RESTORE STEPS VERIFIED"
}
finally {
    if ($con.State -eq "Open") { $con.Close() }
}
