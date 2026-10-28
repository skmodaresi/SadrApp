using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace SadrSetup;

/// <summary>All offline setup steps, in execution order.</summary>
internal static class Steps
{
    public const string LocalDbInstance = "MSSQLLocalDB"; // LocalDB default instance

    public static bool DotNetInstalled()
    {
        // The WPF app targets net10.0-windows with the desktop runtime.
        var root = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
        if (root is null) return false;
        foreach (var name in root.GetSubKeyNames())
        {
            if (Version.TryParse(name, out var v) && v.Major >= 10) return true;
        }
        return false;
    }

    public static void InstallDotNet(string exePath, Action<string> report)
    {
        report("در حال نصب .NET Desktop Runtime 10 (چند دقیقه طول می‌کشد)...");
        Run(exePath, "/install /quiet /norestart", 900, "dotnet runtime");
    }

    public static bool LocalDbInstalled()
    {
        var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions");
        if (key is null) return false;
        foreach (var name in key.GetSubKeyNames())
        {
            if (Version.TryParse(name, out var v) && v.Major >= 15) return true; // 15.x = 2022
        }
        return false;
    }

    public static void InstallLocalDb(string msiPath, Action<string> report)
    {
        report("در حال نصب SQL Server 2022 LocalDB (چند دقیقه طول می‌کشد)...");
        var psi = new ProcessStartInfo("msiexec.exe",
            $"/i \"{msiPath}\" /qn IACCEPTSQLLOCALDBLICENSETERMS=YES /norestart")
        {
            UseShellExecute = true
        };
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(900_000))
            throw new TimeoutException("نصب LocalDB بیش از حد طول کشید.");
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"نصب LocalDB ناموفق بود (کد {p.ExitCode}).");
        SetupLog.Info("SqlLocalDB MSI installed");
    }

    // ------------------------------------------------------------------
    // Existing-install detection (update mode)
    // ------------------------------------------------------------------

    /// <summary>
    /// Looks for a previously installed SadrApp: the registry marker written by this
    /// wizard, the Start-Menu shortcut target, and the well-known Program Files folders.
    /// Returns the install directory or null when nothing is installed yet.
    /// </summary>
    public static string? FindExistingInstall()
    {
        var candidates = new List<string>();

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SadrApp");
            var reg = key?.GetValue("InstallLocation") as string;
            if (reg != null && reg.Length > 0) candidates.Add(reg);
        }
        catch (Exception ex)
        {
            SetupLog.Error("FindExistingInstall registry: " + ex.Message);
        }

        // Older installs (before the registry marker existed) created a Start-Menu
        // shortcut; its target tells us where the app actually lives.
        try
        {
            var lnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs", "سامانه صدر", "سامانه صدر.lnk");
            if (File.Exists(lnk))
            {
                var type = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8"));
                dynamic shell = Activator.CreateInstance(type)!;
                var target = (string)shell.CreateShortcut(lnk).TargetPath;
                var dir = Path.GetDirectoryName(target);
                if (dir != null && dir.Length > 0) candidates.Add(dir);
            }
        }
        catch (Exception ex)
        {
            SetupLog.Error("FindExistingInstall shortcut: " + ex.Message);
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SadrApp"));
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SadrApp"));

        foreach (var c in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(c) &&
                    File.Exists(Path.Combine(c, "SadrApp.exe")))
                    return Path.GetFullPath(c);
            }
            catch
            {
                // ignore malformed candidate paths
            }
        }
        return null;
    }

    /// <summary>Remembers the install location so the next setup run can offer an update.</summary>
    public static void RegisterInstallLocation(string installDir)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SadrApp");
            key?.SetValue("InstallLocation", installDir);
            SetupLog.Info("InstallLocation registered: " + installDir);
        }
        catch (Exception ex)
        {
            SetupLog.Error("RegisterInstallLocation: " + ex.Message); // non-fatal
        }
    }

    /// <summary>
    /// Copies the payload, skipping files that are already identical (same size and
    /// last-write time) so an update only transfers what changed. Connection.dat is
    /// never touched here — it belongs to the user's existing configuration.
    /// </summary>
    public static void CopyPayload(string sourceDir, string targetDir, Action<string> report)
    {
        report("در حال کپی فایل‌های برنامه (فقط فایل‌های تغییرکرده)...");
        Directory.CreateDirectory(targetDir);
        int copied = 0, skipped = 0;
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = GetRelativePath(sourceDir, file);
            if (rel.Equals("Connection.dat", StringComparison.OrdinalIgnoreCase))
            {
                skipped++; // user configuration — never overwrite from the payload
                continue;
            }
            var dest = Path.Combine(targetDir, rel);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            var src = new FileInfo(file);
            var dst = new FileInfo(dest);
            if (dst.Exists && dst.Length == src.Length &&
                dst.LastWriteTimeUtc == src.LastWriteTimeUtc)
            {
                skipped++;
                continue;
            }
            File.Copy(file, dest, overwrite: true);
            copied++;
        }
        report($"کپی فایل‌ها: {copied} فایل به‌روزرسانی شد، {skipped} فایل بدون تغییر باقی ماند.");
    }

    // ------------------------------------------------------------------
    // SQL Server discovery / connection
    // ------------------------------------------------------------------

    /// <summary>
    /// Enumerates locally installed SQL Server engines from the registry
    /// (both 64- and 32-bit views) and returns connection names like
    /// "MACHINENAME" (default instance) or "MACHINENAME\SQLEXPRESS".
    /// </summary>
    public static List<string> DiscoverSqlServerInstances()
    {
        var found = new List<string>();
        var machine = Environment.MachineName;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (key is null) continue;
                foreach (var instance in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(key.GetValue(instance) as string)) continue;
                    var conn = instance.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                        ? machine
                        : machine + "\\" + instance;
                    if (!found.Contains(conn, StringComparer.OrdinalIgnoreCase))
                        found.Add(conn);
                }
            }
            catch (Exception ex)
            {
                SetupLog.Error("DiscoverSqlServerInstances: " + ex.Message);
            }
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        SetupLog.Info("SQL Server instances found: " +
                      (found.Count == 0 ? "(none)" : string.Join(", ", found)));
        return found;
    }

    /// <summary>Builds the connection string in exactly the format the app itself uses.</summary>
    public static string BuildConnectionString(
        string server, string database, bool windowsAuth, string user, string password)
    {
        var sb = new StringBuilder();
        sb.Append("Data Source=").Append(server).Append(';');
        sb.Append("Initial Catalog=").Append(database).Append(';');
        if (windowsAuth)
            sb.Append("Integrated Security=True;");
        else
            sb.Append("Persist Security Info=True;User ID=").Append(user)
              .Append(";Password=").Append(password).Append(';');
        sb.Append("Pooling=False;MultipleActiveResultSets=False;Encrypt=True;")
          .Append("TrustServerCertificate=True;Command Timeout=0;Application Name=SadrApp");
        return sb.ToString();
    }

    public static bool TryConnect(string connectionString, out string error)
    {
        try
        {
            using var con = new SqlConnection(connectionString);
            con.Open();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SetupLog.Error("TryConnect: " + ex.Message);
            return false;
        }
    }

    /// <summary>Lists user databases (system databases excluded) on an already-open server.</summary>
    public static List<string> ListUserDatabases(string masterConnectionString)
    {
        var list = new List<string>();
        using var con = new SqlConnection(masterConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0 ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>Creates the database when it does not exist yet, then verifies it opens.</summary>
    public static void EnsureDatabase(
        string server, string database, bool windowsAuth, string user, string password,
        Action<string> report)
    {
        report("در حال بررسی/ساخت پایگاه داده «" + database + "»...");
        var masterCs = BuildConnectionString(server, "master", windowsAuth, user, password);
        using (var con = new SqlConnection(masterCs))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                $"IF DB_ID(N'{database.Replace("'", "''")}') IS NULL CREATE DATABASE [{database.Replace("]", "]]")}]";
            cmd.CommandTimeout = 0;
            cmd.ExecuteNonQuery();
        }

        // Final connectivity check exactly as the app will connect.
        var appCs = BuildConnectionString(server, database, windowsAuth, user, password);
        using (var con = new SqlConnection(appCs))
        {
            con.Open();
        }
        SetupLog.Info($"database '{database}' on '{server}' verified");
    }

    /// <summary>Reads server/database/auth from an existing Connection.dat (for update-mode logging).</summary>
    public static string[] ReadConnectionInfo(string file)
    {
        try
        {
            var doc = XDocument.Load(file);
            var cs = doc.Root?.Element("ConnectionString")?.Value ?? "";
            var server = GetPart(cs, "Data Source");
            var db = GetPart(cs, "Initial Catalog");
            var integrated = GetPart(cs, "Integrated Security");
            var auth = integrated.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       integrated.Equals("SSPI", StringComparison.OrdinalIgnoreCase)
                ? "ویندوز"
                : "SQL Server";
            return new[] { server, db, auth };
        }
        catch (Exception ex)
        {
            SetupLog.Error("ReadConnectionInfo: " + ex.Message);
            return new[] { "?", "?", "?" };
        }
    }

    private static string GetPart(string cs, string key)
    {
        foreach (var part in cs.Split(';'))
        {
            var idx = part.IndexOf('=');
            if (idx > 0 && part.Substring(0, idx).Trim()
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
                return part.Substring(idx + 1).Trim();
        }
        return "";
    }

    // ------------------------------------------------------------------
    // Connection.dat / database / prerequisites
    // ------------------------------------------------------------------

    public static void WriteConnectionDat(string installDir, string connectionString)
    {
        var doc = new XDocument(
            new XComment(
                "تنظیمات اتصال به پایگاه داده SQL Server - این فایل را روی سیستم مشتری ویرایش کنید"),
            new XElement("DatabaseConnection",
                new XElement("ConnectionString", connectionString)));
        File.WriteAllText(Path.Combine(installDir, "Connection.dat"), doc.ToString());
        SetupLog.Info("Connection.dat written");
    }

    /// <summary>Ensures the LocalDB default instance exists and is running.</summary>
    public static void EnsureLocalDbRunning(Action<string> report)
    {
        report("در حال راه‌اندازی نمونه LocalDB...");
        var exe = FindSqlLocalDbExe();
        RunTolerant(exe, $"c {LocalDbInstance}", 60);
        RunTolerant(exe, $"s {LocalDbInstance}", 60);
    }

    public static void CreateShortcut(string exePath)
    {
        try
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
            var dir = Path.Combine(startMenu, "Programs", "سامانه صدر");
            Directory.CreateDirectory(dir);
            var lnk = Path.Combine(dir, "سامانه صدر.lnk");

            var type = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); // WScript.Shell
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath)!;
            shortcut.Description = "سامانه حسابداری و مدیریت پروژه صدر";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            SetupLog.Error("shortcut: " + ex.Message); // non-fatal
        }
    }

    /// <summary>Path.GetRelativePath replacement for .NET Framework 4.7.2.</summary>
    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(AppendSeparator(Path.GetFullPath(basePath)));
        var fileUri = new Uri(Path.GetFullPath(fullPath));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendSeparator(string p) =>
        p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p : p + Path.DirectorySeparatorChar;

    private static string FindSqlLocalDbExe()
    {
        // Prefer PATH, then the known install locations.
        var candidates = new[]
        {
            @"C:\Program Files\Microsoft SQL Server\150\Tools\Binn\SqlLocalDB.exe",
            @"C:\Program Files\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe"
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "sqllocaldb.exe";
    }

    private static void Run(string fileName, string args, int timeoutSec, string label)
    {
        var psi = new ProcessStartInfo(fileName, args) { UseShellExecute = true };
        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(timeoutSec * 1000))
            throw new TimeoutException($"{label} exceeded {timeoutSec}s.");
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{label} failed (exit {p.ExitCode}).");
        SetupLog.Info($"{label} ok (exit {p.ExitCode})");
    }

    /// <summary>Runs a helper tool, tolerating non-zero exits (already exists/running).</summary>
    private static void RunTolerant(string fileName, string args, int timeoutSec)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutSec * 1000);
            SetupLog.Info($"{fileName} {args} -> exit {p.ExitCode}: {output.Trim()}");
        }
        catch (Exception ex)
        {
            SetupLog.Error($"tolerant run {fileName} {args}: {ex.Message}");
        }
    }
}
