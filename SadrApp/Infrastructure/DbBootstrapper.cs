using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// First-run database bootstrap: creates the database if missing, generates the EF schema
/// (EnsureCreated), and provides the admin-user creation used by the first-run wizard.
/// Called from App.OnStartup before seeding/login, and safe to call on every start.
/// </summary>
public static class DbBootstrapper
{
    public static string DatabaseName
    {
        get
        {
            var cs = Database.ConnectionString;
            var b = new SqlConnectionStringBuilder(cs);
            return string.IsNullOrWhiteSpace(b.InitialCatalog) ? "SadrTest" : b.InitialCatalog;
        }
    }

    /// <summary>Creates the database on the configured server when missing, then the EF schema.</summary>
    public static void EnsureDatabase()
    {
        var b = new SqlConnectionStringBuilder(Database.ConnectionString);
        var dbName = DatabaseName;

        // 1) Create the database itself against master (works for LocalDB and full SQL Server).
        var master = new SqlConnectionStringBuilder(b.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        using (var con = new SqlConnection(master))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{dbName.Replace("'", "''")}') IS NULL CREATE DATABASE [{dbName.Replace("]", "]]")}]";
            cmd.CommandTimeout = 0;
            cmd.ExecuteNonQuery();
        }

        // 2) Generate tables/keys from the EF model (no-op when the schema already exists).
        using (var db = SadrDb.New())
        {
            db.Database.EnsureCreated();
        }

        // 3) Schema fixes for tables that predate the EF model. Fresh installs get these
        //    columns from the model itself; existing databases get them added here.
        using (var con = new SqlConnection(b.ConnectionString))
        {
            con.Open();
            // Cheques: received-from-customer (1) vs issued-to-provider (2) direction.
            SchemaHelpers.AddColumnIfMissing(con, "Cheques", "Direction", "int NOT NULL DEFAULT(1)");
            // Products: opening stock for warehouse management.
            SchemaHelpers.AddColumnIfMissing(con, "Products", "InitialQuantity", "decimal(18,4) NOT NULL DEFAULT(0)");
            // Warehouse transfers: product movements, pending until a warehouse keeper accepts them.
            SchemaHelpers.AddTableIfMissing(con, "WarehouseTransfers", """
                CREATE TABLE [dbo].[WarehouseTransfers] (
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
                    CONSTRAINT [PK_WarehouseTransfers] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_WarehouseTransfers_ProductId] ON [dbo].[WarehouseTransfers] ([ProductId]);
                CREATE INDEX [IX_WarehouseTransfers_InvoiceId] ON [dbo].[WarehouseTransfers] ([InvoiceId]);
                """,
                fkName: "FK_WarehouseTransfers_Invoices_InvoiceId",
                fkDefinition: "CONSTRAINT [FK_WarehouseTransfers_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id])");
            // Bank-transaction ledger: cheque cashing/payments post here and drive balances.
            SchemaHelpers.AddTableIfMissing(con, "BankTransactions", """
                CREATE TABLE [dbo].[BankTransactions] (
                    [Id] int NOT NULL IDENTITY,
                    [BankAccountId] int NOT NULL,
                    [Kind] int NOT NULL,
                    [Amount] decimal(18,4) NOT NULL,
                    [Date] nvarchar(max) NOT NULL,
                    [DateG] datetime2 NOT NULL,
                    [Source] int NOT NULL,
                    [ChequeId] int NULL,
                    [EventKind] nvarchar(max) NULL,
                    [Describtion] nvarchar(max) NOT NULL,
                    [Deleted] bit NOT NULL,
                    [RecordUniqueId] uniqueidentifier NOT NULL,
                    [CreateUserId] uniqueidentifier NULL,
                    [UpdateUserId] uniqueidentifier NULL,
                    [CreateDateTime] datetime2 NULL,
                    [UpdateDateTime] datetime2 NULL,
                    CONSTRAINT [PK_BankTransactions] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_BankTransactions_BankAccountId] ON [dbo].[BankTransactions] ([BankAccountId]);
                CREATE INDEX [IX_BankTransactions_ChequeId] ON [dbo].[BankTransactions] ([ChequeId]);
                """,
                fkName: "FK_BankTransactions_Cheques_ChequeId",
                fkDefinition: "CONSTRAINT [FK_BankTransactions_Cheques_ChequeId] FOREIGN KEY ([ChequeId]) REFERENCES [Cheques] ([Id])");

            // Cash boxes and money transactions. The live database already has these
            // tables; this only creates them on fresh installs.
            SchemaHelpers.AddTableIfMissing(con, "Cashes", """
                CREATE TABLE [dbo].[Cashes] (
                    [Id] int NOT NULL IDENTITY,
                    [Name] nvarchar(max) NOT NULL,
                    [Description] nvarchar(max) NULL,
                    [StartBalance] decimal(18,4) NOT NULL,
                    [CurrentBalance] decimal(18,4) NOT NULL,
                    [Status] int NOT NULL,
                    [OwnerPersonId] int NULL,
                    [OwnerCompanyId] int NULL,
                    [ResponcePersonId] int NOT NULL,
                    [SignPeople] nvarchar(max) NULL,
                    [Deleted] bit NOT NULL,
                    [RecordUniqueId] uniqueidentifier NOT NULL,
                    [CreateUserId] uniqueidentifier NULL,
                    [UpdateUserId] uniqueidentifier NULL,
                    [CreateDateTime] datetime2 NULL,
                    [UpdateDateTime] datetime2 NULL,
                    CONSTRAINT [PK_Cashes] PRIMARY KEY ([Id])
                );
                """);
            SchemaHelpers.AddTableIfMissing(con, "AccountTransactions", """
                CREATE TABLE [dbo].[AccountTransactions] (
                    [Id] int NOT NULL IDENTITY,
                    [BankAccountId] int NOT NULL,
                    [CashId] int NOT NULL,
                    [Date] nvarchar(max) NOT NULL,
                    [DateG] datetime2 NOT NULL,
                    [Value] decimal(18,4) NOT NULL,
                    [Description] nvarchar(max) NOT NULL,
                    [InvoiceId] int NULL,
                    [PersonId] int NULL,
                    [CompnayId] int NULL,
                    [Type] int NOT NULL,
                    [TransActionSystem] int NOT NULL,
                    [Deleted] bit NOT NULL,
                    [RecordUniqueId] uniqueidentifier NOT NULL,
                    [CreateUserId] uniqueidentifier NULL,
                    [UpdateUserId] uniqueidentifier NULL,
                    [CreateDateTime] datetime2 NULL,
                    [UpdateDateTime] datetime2 NULL,
                    CONSTRAINT [PK_AccountTransactions] PRIMARY KEY ([Id])
                );
                """);
            SchemaHelpers.AddTableIfMissing(con, "TaskReportMoneyTransactions", """
                CREATE TABLE [dbo].[TaskReportMoneyTransactions] (
                    [Id] int NOT NULL IDENTITY,
                    [TaskReportId] int NOT NULL,
                    [AccountTransactionId] int NOT NULL,
                    [Deleted] bit NOT NULL,
                    [RecordUniqueId] uniqueidentifier NOT NULL,
                    [CreateUserId] uniqueidentifier NULL,
                    [UpdateUserId] uniqueidentifier NULL,
                    [CreateDateTime] datetime2 NULL,
                    [UpdateDateTime] datetime2 NULL,
                    CONSTRAINT [PK_TaskReportMoneyTransactions] PRIMARY KEY ([Id])
                );
                """);
            SchemaHelpers.AddTableIfMissing(con, "InvoiceMoneyTransactions", """
                CREATE TABLE [dbo].[InvoiceMoneyTransactions] (
                    [Id] int NOT NULL IDENTITY,
                    [InvoiceId] int NOT NULL,
                    [AccountTransactionId] int NOT NULL,
                    [Deleted] bit NOT NULL,
                    [RecordUniqueId] uniqueidentifier NOT NULL,
                    [CreateUserId] uniqueidentifier NULL,
                    [UpdateUserId] uniqueidentifier NULL,
                    [CreateDateTime] datetime2 NULL,
                    [UpdateDateTime] datetime2 NULL,
                    CONSTRAINT [PK_InvoiceMoneyTransactions] PRIMARY KEY ([Id])
                );
                """);

            // Helpful indexes on the pre-existing money tables (idempotent, no FK changes
            // on the live database).
            foreach (var (table, index, col) in new[]
            {
                ("AccountTransactions", "IX_AccountTransactions_BankAccountId", "BankAccountId"),
                ("AccountTransactions", "IX_AccountTransactions_CashId", "CashId"),
                ("AccountTransactions", "IX_AccountTransactions_InvoiceId", "InvoiceId"),
                ("TaskReportMoneyTransactions", "IX_TaskReportMoneyTransactions_TaskReportId", "TaskReportId"),
                ("TaskReportMoneyTransactions", "IX_TaskReportMoneyTransactions_AccountTransactionId", "AccountTransactionId"),
                ("InvoiceMoneyTransactions", "IX_InvoiceMoneyTransactions_InvoiceId", "InvoiceId"),
                ("InvoiceMoneyTransactions", "IX_InvoiceMoneyTransactions_AccountTransactionId", "AccountTransactionId")
            })
            {
                using var idx = con.CreateCommand();
                idx.CommandText =
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @i AND object_id = OBJECT_ID(@t)) " +
                    "EXEC(N'CREATE INDEX ' + QUOTENAME(@i) + N' ON dbo.' + QUOTENAME(PARSENAME(@t,1)) + N' (' + QUOTENAME(@c) + N')');";
                idx.Parameters.AddWithValue("@i", index);
                idx.Parameters.AddWithValue("@t", $"dbo.{table}");
                idx.Parameters.AddWithValue("@c", col);
                idx.ExecuteNonQuery();
            }

            // AccountTransactions upgrade for databases created before this release.
            // The original script made BankAccountId/CashId NOT NULL and pointed
            // BankAccountId at the legacy empty Accounts table. A transaction lives in
            // either a bank account or a cash box, so: relax both columns to NULL, drop
            // the wrong FK, add the correct one to BankAccounts, then fix existing rows.
            // All steps are idempotent and touch no data.
            using var tx = con.CreateCommand();
            tx.CommandText = """
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
                               AND name = 'BankAccountId' AND is_nullable = 1)   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys fk
                   JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                   WHERE fk.parent_object_id = OBJECT_ID('dbo.AccountTransactions')
                     AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = 'BankAccountId')
                    ALTER TABLE dbo.AccountTransactions ADD CONSTRAINT FK_AccountTransactions_BankAccounts_BankAccountId
                        FOREIGN KEY (BankAccountId) REFERENCES dbo.BankAccounts (Id);
                """;
            tx.CommandTimeout = 0;
            tx.ExecuteNonQuery();

            // Normalize rows written by earlier builds that used a fake counterpart id:
            // cash-side rows keep only CashId, bank-side rows keep only BankAccountId.
            using var fix = con.CreateCommand();
            fix.CommandTimeout = 0;
            fix.CommandText = """
                UPDATE dbo.AccountTransactions SET BankAccountId = NULL
                 WHERE TransActionSystem = 1 AND BankAccountId IS NOT NULL;
                UPDATE dbo.AccountTransactions SET CashId = NULL
                 WHERE TransActionSystem = 2 AND CashId IS NOT NULL;
                """;
            fix.ExecuteNonQuery();
        }
    }

    /// <summary>True when at least one login user already exists in the database.</summary>
    public static bool AdminUserExists()
    {
        using var db = SadrDb.New();
        return db.Users.Any();
    }

    /// <summary>Creates the first administrator account (first-run wizard).</summary>
    public static void CreateAdminUser(string username, string password, string? email)
    {
        using var db = SadrDb.New();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? $"{username.Trim()}@local" : email.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            Role = "Admin",
            EmailConfirmed = true
        });
        db.SaveChanges();
    }
}
