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
Write-Host '--- FKs on the four new tables ---'
$dt = Table "SELECT OBJECT_NAME(parent_object_id) AS tbl, name, OBJECT_NAME(referenced_object_id) AS ref FROM sys.foreign_keys WHERE OBJECT_NAME(parent_object_id) IN ('Cashes','AccountTransactions','TaskReportMoneyTransactions','InvoiceMoneyTransactions')"
foreach ($r in $dt.Rows) { Write-Host ("{0}: {1} -> {2}" -f $r['tbl'], $r['name'], $r['ref']) }
if ($dt.Rows.Count -eq 0) { Write-Host '(none)' }
Write-Host '--- TaskReports columns ---'
$dt2 = Table "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TaskReports' ORDER BY ORDINAL_POSITION"
foreach ($r in $dt2.Rows) { Write-Host ("{0} | {1} | null={2}" -f $r['COLUMN_NAME'], $r['DATA_TYPE'], $r['IS_NULLABLE']) }
