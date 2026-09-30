$ErrorActionPreference = 'Stop'
$cs = 'Data Source=.\SQLExpress;Initial Catalog=SadrTest;Integrated Security=True;TrustServerCertificate=True'
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()

# Mirrors the AccountTransactions upgrade inside DbBootstrapper.EnsureDatabase:
# relax BankAccountId/CashId to NULL, drop the wrong FK to legacy Accounts,
# add the correct FK to BankAccounts, normalize rows. All idempotent.
$sql = @"
IF COL_LENGTH('dbo.AccountTransactions', 'BankAccountId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountTransactions')
               AND name = 'BankAccountId' AND is_nullable = 0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountTransactions_Accounts_BankAccountId')
        ALTER TABLE dbo.AccountTransactions DROP CONSTRAINT FK_AccountTransactions_Accounts_BankAccountId;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountTransactions_BankAccounts_BankAccountId')
        ALTER TABLE dbo.AccountTransactions DROP CONSTRAINT FK_AccountTransactions_BankAccounts_BankAccountId;
    ALTER TABLE dbo.AccountTransactions ALTER COLUMN BankAccountId int NULL;
    ALTER TABLE dbo.AccountTransactions ALTER COLUMN CashId int NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountTransactions_BankAccounts_BankAccountId')
        ALTER TABLE dbo.AccountTransactions ADD CONSTRAINT FK_AccountTransactions_BankAccounts_BankAccountId
            FOREIGN KEY (BankAccountId) REFERENCES dbo.BankAccounts (Id);
END
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AccountTransactions_BankAccounts_BankAccountId')
   AND COL_LENGTH('dbo.AccountTransactions', 'BankAccountId') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AccountTransactions')
               AND name = 'BankAccountId' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk
                   JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                   WHERE fk.parent_object_id = OBJECT_ID('dbo.AccountTransactions')
                     AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = 'BankAccountId')
    ALTER TABLE dbo.AccountTransactions ADD CONSTRAINT FK_AccountTransactions_BankAccounts_BankAccountId
        FOREIGN KEY (BankAccountId) REFERENCES dbo.BankAccounts (Id);
UPDATE dbo.AccountTransactions SET BankAccountId = NULL
 WHERE TransActionSystem = 1 AND BankAccountId IS NOT NULL;
UPDATE dbo.AccountTransactions SET CashId = NULL
 WHERE TransActionSystem = 2 AND CashId IS NOT NULL;
"@
$cmd = $con.CreateCommand()
$cmd.CommandText = $sql
$cmd.CommandTimeout = 0
$cmd.ExecuteNonQuery() | Out-Null
Write-Host 'Upgrade executed OK.'

# verify
function Table($q) {
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($q, $con)
    $dt = New-Object System.Data.DataTable
    $da.Fill($dt) | Out-Null
    return , $dt
}
Write-Host '--- AccountTransactions columns after upgrade ---'
$dt = Table "SELECT COLUMN_NAME, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='AccountTransactions' AND COLUMN_NAME IN ('BankAccountId','CashId') ORDER BY ORDINAL_POSITION"
foreach ($r in $dt.Rows) { Write-Host ("{0} | null={1}" -f $r['COLUMN_NAME'], $r['IS_NULLABLE']) }
Write-Host '--- AccountTransactions FKs after upgrade ---'
$dt = Table "SELECT name, OBJECT_NAME(referenced_object_id) AS ref FROM sys.foreign_keys WHERE OBJECT_NAME(parent_object_id)='AccountTransactions'"
foreach ($r in $dt.Rows) { Write-Host ("{0} -> {1}" -f $r['name'], $r['ref']) }
