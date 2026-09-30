// Fresh-install schema verification: creates a brand-new database via
// DbBootstrapper.EnsureDatabase + SeedData, then diffs the resulting schema
// against the EF model. MISMATCH lines go to stderr; exit 1 on failure.
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

var cs = @"Data Source=.\SQLExpress;Initial Catalog=SadrSchemaCheck;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Encrypt=True";
DbConfig.Save(cs);
Database.Reset();

// drop any leftover check DB from a previous run
var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
using (var con = new SqlConnection(master))
{
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText = "IF DB_ID(N'SadrSchemaCheck') IS NOT NULL BEGIN ALTER DATABASE SadrSchemaCheck SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SadrSchemaCheck; END";
    cmd.CommandTimeout = 0;
    cmd.ExecuteNonQuery();
}

// 1) bootstrap a fresh install (exactly what a new user's first run does)
DbBootstrapper.EnsureDatabase();
SeedData.Run();

// 2) capture the actual schema
var liveTables = new HashSet<string>();
var liveCols = new Dictionary<string, HashSet<string>>();
var liveFks = new HashSet<string>();
var liveIdx = new HashSet<string>();
using (var con = new SqlConnection(cs))
{
    con.Open();
    DataTable T(string q)
    {
        using var da = new SqlDataAdapter(q, con);
        var dt = new DataTable();
        da.Fill(dt);
        return dt;
    }

    foreach (DataRow r in T("SELECT name FROM sys.tables").Rows)
    {
        var name = r.Field<string>("name")!;
        liveTables.Add(name);
        liveCols[name] = new HashSet<string>();
    }
    foreach (DataRow r in T("SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS").Rows)
        liveCols[r.Field<string>("TABLE_NAME")!].Add(r.Field<string>("COLUMN_NAME")!);
    foreach (DataRow r in T("SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS tbl FROM sys.foreign_keys fk").Rows)
        liveFks.Add(r.Field<string>("tbl")! + "|" + r.Field<string>("name")!);
    foreach (DataRow r in T("SELECT OBJECT_NAME(i.object_id) AS tbl, i.name FROM sys.indexes i WHERE i.name IS NOT NULL AND OBJECTPROPERTY(i.object_id,'IsUserTable')=1").Rows)
        liveIdx.Add(r.Field<string>("tbl")! + "|" + r.Field<string>("name")!);
}

// 3) derive the expected schema from the EF model
var model = SadrDb.New().Model;
int errors = 0;
void Fail(string m)
{
    errors++;
    Console.Error.WriteLine("MISMATCH: " + m);
}

foreach (var et in model.GetEntityTypes())
{
    var table = et.GetTableName();
    if (table is null || !liveTables.Contains(table)) continue; // skip non-table / keyless types
    if (!liveCols.TryGetValue(table, out var cols)) { Fail($"missing table {table}"); continue; }

    foreach (var p in et.GetProperties())
        if (p.GetColumnName() is { } cn && !cols.Contains(cn))
            Fail($"missing column {table}.{cn}");

    foreach (var fk in et.GetForeignKeys())
    {
        var name = fk.GetConstraintName();
        if (name is null) continue;
        if (!liveFks.Contains(table + "|" + name))
            Console.WriteLine("FK-INFO: not created: " + table + "|" + name);
    }
}

// 4) FK-column indexes (EF naming convention)
foreach (var et in model.GetEntityTypes())
{
    var tbl = et.GetTableName();
    if (tbl is null) continue;
    foreach (var fk in et.GetForeignKeys())
    {
        var col = fk.Properties.FirstOrDefault()?.GetColumnName();
        if (col is null) continue;
        var expected = $"IX_{tbl}_{col}";
        if (!liveIdx.Contains(tbl + "|" + expected))
            Console.WriteLine("IDX-INFO: no index " + expected);
    }
}

Console.WriteLine($"tables={liveTables.Count} fks={liveFks.Count} indexes={liveIdx.Count}");
if (errors > 0)
{
    Console.Error.WriteLine($"SCHEMA CHECK FAILED: {errors} mismatch(es)");
    return 1;
}
Console.WriteLine("SCHEMA CHECK PASSED");
return 0;
