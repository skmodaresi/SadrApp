using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SadrApp.Infrastructure;

/// <summary>Shared open EF connection + T-SQL helpers used by forms and the settings window.</summary>
public static class Database
{
    private static string? _connectionString;

    public static string ConnectionString =>
        _connectionString ??= DbConfig.Load();

    /// <summary>Call after the user edits Connection.dat/settings window.</summary>
    public static void Reset() => _connectionString = null;

    public static SqlConnection OpenConnection()
    {
        var con = new SqlConnection(ConnectionString);
        con.Open();
        return con;
    }

    public static DataTable Query(string sql, params SqlParameter[] parameters)
    {
        using var con = OpenConnection();
        using var cmd = new SqlCommand(sql, con);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        using var da = new SqlDataAdapter(cmd);
        var dt = new DataTable();
        da.Fill(dt);
        return dt;
    }

    public static object? Scalar(string sql, params SqlParameter[] parameters)
    {
        using var con = OpenConnection();
        using var cmd = new SqlCommand(sql, con);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        return cmd.ExecuteScalar();
    }

    public static int Execute(string sql, params SqlParameter[] parameters)
    {
        using var con = OpenConnection();
        using var cmd = new SqlCommand(sql, con);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>True when a TCP/SQL connection can actually be opened with the current string.</summary>
    public static bool TestConnection(string connectionString, out string error)
    {
        try
        {
            using var con = new SqlConnection(connectionString);
            con.Open();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ServerVersionInfo()
    {
        var dt = Query("SELECT @@SERVERNAME AS ServerName, @@VERSION AS Ver");
        return $"{dt.Rows[0]["ServerName"]}\n{dt.Rows[0]["Ver"]}";
    }
}
