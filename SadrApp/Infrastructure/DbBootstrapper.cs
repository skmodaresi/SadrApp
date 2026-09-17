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
