$ErrorActionPreference = 'Stop'
$cs = 'Data Source=.\SQLExpress;Initial Catalog=SadrTest;Integrated Security=True;TrustServerCertificate=True'
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
foreach ($t in @('Cashes','AccountTransactions','TaskReportMoneyTransactions','InvoiceMoneyTransactions')) {
    Write-Host "===== $t ====="
    $da = New-Object System.Data.SqlClient.SqlDataAdapter("SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '$t' ORDER BY ORDINAL_POSITION", $con)
    $dt = New-Object System.Data.DataTable
    $da.Fill($dt) | Out-Null
    foreach ($r in $dt.Rows) { Write-Host ("{0} | {1} | null={2} | def={3}" -f $r['COLUMN_NAME'], $r['DATA_TYPE'], $r['IS_NULLABLE'], $r['COLUMN_DEFAULT']) }
}
