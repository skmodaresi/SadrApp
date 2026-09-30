$ErrorActionPreference = 'Stop'
$cs = 'Data Source=.\SQLExpress;Initial Catalog=SadrTest;Integrated Security=True;TrustServerCertificate=True'
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
function Table($sql) {
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($sql, $con)
    $dt = New-Object System.Data.DataTable
    $da.Fill($dt) | Out-Null
    return , $dt
}
Write-Host '--- tables like %ccount% ---'
$dt = Table "SELECT t.name AS tbl, p.rows AS rowcnt FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1) WHERE t.name LIKE '%ccount%' ORDER BY t.name"
foreach ($r in $dt.Rows) { Write-Host ("{0}: {1} rows" -f $r['tbl'], $r['rowcnt']) }
Write-Host '--- Accounts columns ---'
$dt = Table "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Accounts' ORDER BY ORDINAL_POSITION"
foreach ($r in $dt.Rows) { Write-Host ("{0} | {1}" -f $r['COLUMN_NAME'], $r['DATA_TYPE']) }
Write-Host '--- BankAccounts columns ---'
$dt = Table "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='BankAccounts' ORDER BY ORDINAL_POSITION"
foreach ($r in $dt.Rows) { Write-Host $r['COLUMN_NAME'] }
Write-Host '--- AccountTransactions FK status (is_disabled, is_not_trusted) ---'
$dt = Table "SELECT name, is_disabled, is_not_trusted FROM sys.foreign_keys WHERE OBJECT_NAME(parent_object_id) IN ('AccountTransactions','Cashes','TaskReportMoneyTransactions','InvoiceMoneyTransactions')"
foreach ($r in $dt.Rows) { Write-Host ("{0} | disabled={1} | not_trusted={2}" -f $r['name'], $r['is_disabled'], $r['is_not_trusted']) }
Write-Host '--- Accounts sample (top 5) ---'
try {
    $dt = Table "SELECT TOP 5 Id, Name FROM Accounts ORDER BY Id"
    foreach ($r in $dt.Rows) { Write-Host ("{0} | {1}" -f $r['Id'], $r['Name']) }
} catch { Write-Host ('(no Id/Name): ' + $_.Exception.Message) }
Write-Host '--- BankAccounts sample (top 5) ---'
try {
    $dt = Table "SELECT TOP 5 Id, Name FROM BankAccounts ORDER BY Id"
    foreach ($r in $dt.Rows) { Write-Host ("{0} | {1}" -f $r['Id'], $r['Name']) }
} catch { Write-Host ('(error): ' + $_.Exception.Message) }
Write-Host '--- money-table indexes present ---'
$dt = Table "SELECT OBJECT_NAME(object_id) AS tbl, name FROM sys.indexes WHERE OBJECT_NAME(object_id) IN ('AccountTransactions','TaskReportMoneyTransactions','InvoiceMoneyTransactions') AND name LIKE 'IX_%'"
foreach ($r in $dt.Rows) { Write-Host ("{0}: {1}" -f $r['tbl'], $r['name']) }
