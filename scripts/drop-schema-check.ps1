param([string]$Database = 'SadrSchemaCheck')
$ErrorActionPreference = 'Stop'
$cs = 'Data Source=.\SQLExpress;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True'
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()
$cmd = $con.CreateCommand()
$cmd.CommandText = "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE $Database SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $Database; END"
$cmd.ExecuteNonQuery() | Out-Null
$cmd2 = $con.CreateCommand()
$cmd2.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = '$Database'"
if ([int]$cmd2.ExecuteScalar() -eq 0) { Write-Host "$Database dropped" } else { Write-Host "$Database still present" }
