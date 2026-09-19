using Microsoft.Data.SqlClient;

namespace SadrApp.Infrastructure;

/// <summary>Small idempotent DDL helpers for upgrading pre-existing databases.</summary>
public static class SchemaHelpers
{
    /// <summary>Adds a column when it does not already exist (safe to call on every start).
    /// <paramref name="definition"/> is the type part only, e.g. "int NOT NULL DEFAULT(1)".</summary>
    public static void AddColumnIfMissing(SqlConnection con, string table, string column, string definition)
    {
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText =
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@t) AND name = @c) " +
                "BEGIN " +
                "DECLARE @s nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(PARSENAME(@t,1)) + N' ADD ' + QUOTENAME(@c) + N' ' + @d; " +
                "EXEC(@s); " +
                "END";
            cmd.Parameters.AddWithValue("@t", $"dbo.{table}");
            cmd.Parameters.AddWithValue("@c", column);
            cmd.Parameters.AddWithValue("@d", definition);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Creates a table when it does not already exist (safe to call on every start), and
    /// when <paramref name="fkName"/>/<paramref name="fkDefinition"/> are given, adds that
    /// foreign key when the table exists without it. FK definitions must include a leading
    /// "CONSTRAINT [name] FOREIGN KEY (...) REFERENCES ...".
    /// </summary>
    public static void AddTableIfMissing(SqlConnection con, string table, string createSql,
        string? fkName = null, string? fkDefinition = null)
    {
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "IF OBJECT_ID(@t) IS NULL EXEC(@sql)";
            cmd.Parameters.AddWithValue("@t", $"dbo.{table}");
            cmd.Parameters.AddWithValue("@sql", createSql);
            cmd.ExecuteNonQuery();
        }
        if (fkName is null || fkDefinition is null) return;
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText =
                "IF OBJECT_ID(@t) IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@t) AND name = @fk) " +
                "BEGIN DECLARE @s nvarchar(max) = N'ALTER TABLE ' + QUOTENAME(PARSENAME(@t,1)) + N' ADD ' + @fkDef; EXEC(@s); END";
            cmd.Parameters.AddWithValue("@t", $"dbo.{table}");
            cmd.Parameters.AddWithValue("@fk", fkName);
            cmd.Parameters.AddWithValue("@fkDef", fkDefinition);
            cmd.ExecuteNonQuery();
        }
    }
}
