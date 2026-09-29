$ErrorActionPreference = 'Stop'
$cs = 'Data Source=.\SQLExpress;Initial Catalog=SadrTest;Integrated Security=True;TrustServerCertificate=True'
$con = New-Object System.Data.SqlClient.SqlConnection($cs)
$con.Open()

function Exec($sql) {
    $cmd = $con.CreateCommand(); $cmd.CommandText = $sql
    if ($script:tx -ne $null) { $cmd.Transaction = $script:tx }
    $cmd.ExecuteNonQuery() | Out-Null
}
function Scalar($sql) {
    $cmd = $con.CreateCommand(); $cmd.CommandText = $sql
    if ($script:tx -ne $null) { $cmd.Transaction = $script:tx }
    return $cmd.ExecuteScalar()
}
function Table($sql) {
    $da = New-Object System.Data.SqlClient.SqlDataAdapter($sql, $con)
    $dt = New-Object System.Data.DataTable
    $da.Fill($dt) | Out-Null
    return , $dt
}

Write-Host '--- 1) apply schema fixes for real (idempotent, same as app startup) ---'
Exec "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Products') AND name = 'InitialQuantity') ALTER TABLE dbo.Products ADD InitialQuantity decimal(18,4) NOT NULL DEFAULT(0)"
Exec "IF OBJECT_ID('dbo.WarehouseTransfers') IS NULL CREATE TABLE [dbo].[WarehouseTransfers] (
    [Id] int NOT NULL IDENTITY,
    [ProductId] int NOT NULL,
    [WareHouseId] int NULL,
    [Direction] int NOT NULL,
    [Quantity] decimal(18,4) NOT NULL,
    [TransferDate] nvarchar(max) NOT NULL,
    [TransferDateG] datetime2 NOT NULL,
    [Accepted] bit NOT NULL,
    [AcceptedByUserId] uniqueidentifier NULL,
    [InvoiceId] int NULL,
    [PersonId] int NULL,
    [Describtion] nvarchar(max) NOT NULL,
    [Deleted] bit NOT NULL,
    [RecordUniqueId] uniqueidentifier NOT NULL,
    [CreateUserId] uniqueidentifier NULL,
    [UpdateUserId] uniqueidentifier NULL,
    [CreateDateTime] datetime2 NULL,
    [UpdateDateTime] datetime2 NULL,
    CONSTRAINT [PK_WarehouseTransfers] PRIMARY KEY ([Id]));"
Exec "IF OBJECT_ID('dbo.WarehouseTransfers') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_WarehouseTransfers_Invoices_InvoiceId') ALTER TABLE dbo.WarehouseTransfers ADD CONSTRAINT [FK_WarehouseTransfers_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]);"
Exec "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WarehouseTransfers_ProductId' AND object_id = OBJECT_ID('dbo.WarehouseTransfers')) CREATE INDEX [IX_WarehouseTransfers_ProductId] ON [dbo].[WarehouseTransfers] ([ProductId]);"
Exec "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WarehouseTransfers_InvoiceId' AND object_id = OBJECT_ID('dbo.WarehouseTransfers')) CREATE INDEX [IX_WarehouseTransfers_InvoiceId] ON [dbo].[WarehouseTransfers] ([InvoiceId]);"
Write-Host ('column: ' + (Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Products') AND name = 'InitialQuantity'"))
Write-Host ('table: ' + (Scalar "SELECT COUNT(*) FROM sys.tables WHERE name = 'WarehouseTransfers'"))
Write-Host ('fk: ' + (Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_WarehouseTransfers_Invoices_InvoiceId'"))

Write-Host '--- 2) stock semantics simulation (rolled back) ---'
$script:tx = $con.BeginTransaction()
try {
    $cmd = $con.CreateCommand(); $cmd.Transaction = $tx
    $cat = Scalar "SELECT MIN(Id) FROM ProductCategories"
    if (-not $cat) { $cmd.CommandText = "INSERT INTO ProductCategories (Name, Deleted, RecordUniqueId) VALUES (N'__t__', 0, NEWID()); SELECT SCOPE_IDENTITY();"; $cat = $cmd.ExecuteScalar() }
    $unit = Scalar "SELECT MIN(Id) FROM Units"
    if (-not $unit) { $cmd.CommandText = "INSERT INTO Units (Name, ParentPercentRel, Deleted, RecordUniqueId) VALUES (N'__t__', 0, 0, NEWID()); SELECT SCOPE_IDENTITY();"; $unit = $cmd.ExecuteScalar() }
    $cmd.CommandText = "INSERT INTO Products (Name, Code, Description, MainCategoryId, MainUnitId, InitialQuantity, Deleted, RecordUniqueId) OUTPUT INSERTED.Id VALUES (N'__stocktest__', N'__ST__', N'', $cat, $unit, 100, 0, NEWID())"
    $prodId = $cmd.ExecuteScalar()
    $ins = "INSERT INTO WarehouseTransfers (ProductId, Direction, Quantity, TransferDate, TransferDateG, Accepted, Describtion, Deleted, RecordUniqueId) VALUES ($prodId, {0}, {1}, N'1405/07/01', '2026-09-23', {2}, N'test', 0, NEWID())"
    $cmd.CommandText = ($ins -f 1, 20, 0); $cmd.ExecuteNonQuery() | Out-Null   # pending in: must NOT count
    $cmd.CommandText = ($ins -f 2, 30, 1); $cmd.ExecuteNonQuery() | Out-Null   # accepted out
    $cmd.CommandText = ($ins -f 1, 45, 1); $cmd.ExecuteNonQuery() | Out-Null   # accepted in
    $sql = "SELECT p.InitialQuantity + ISNULL(SUM(CASE WHEN t.Accepted = 1 AND t.Direction = 1 THEN t.Quantity WHEN t.Accepted = 1 AND t.Direction = 2 THEN -t.Quantity ELSE 0 END), 0) FROM Products p LEFT JOIN WarehouseTransfers t ON t.ProductId = p.Id AND t.Deleted = 0 WHERE p.Id = $prodId GROUP BY p.InitialQuantity"
    $stock = Scalar $sql
    Write-Host ('stock = ' + $stock + ' (expected 115: 100 + 45 in - 30 out, pending 20 excluded)')
    if ([decimal]$stock -ne 115) { throw 'STOCK MATH WRONG' }
} finally {
    $script:tx.Rollback()
    Write-Host 'rolled back - no data left behind'
}
Write-Host 'ALL WAREHOUSE CHECKS PASSED'
