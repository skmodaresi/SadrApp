using System.Diagnostics;
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

    public static void CopyPayload(string sourceDir, string targetDir, Action<string> report)
    {
        report("در حال کپی فایل‌های برنامه...");
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    public static void WriteConnectionDat(string installDir, string server, string database)
    {
        var cs =
            $"Data Source={server};Initial Catalog={database};Integrated Security=True;" +
            "Persist Security Info=True;Pooling=False;MultipleActiveResultSets=False;" +
            "Encrypt=True;TrustServerCertificate=True;Command Timeout=0";
        var doc = new System.Xml.Linq.XDocument(
            new System.Xml.Linq.XComment(
                "تنظیمات اتصال به پایگاه داده SQL Server - این فایل را روی سیستم مشتری ویرایش کنید"),
            new System.Xml.Linq.XElement("DatabaseConnection",
                new System.Xml.Linq.XElement("ConnectionString", cs)));
        File.WriteAllText(Path.Combine(installDir, "Connection.dat"), doc.ToString());
        SetupLog.Info("Connection.dat written: " + cs);
    }

    /// <summary>Ensures the LocalDB default instance exists/started and the app database exists.</summary>
    public static void CreateDatabase(string database, Action<string> report)
    {
        report("در حال راه‌اندازی نمونه LocalDB...");
        var exe = FindSqlLocalDbExe();

        // create/start are tolerant: they may already exist / be running.
        RunTolerant(exe, $"c {LocalDbInstance}", 60);
        RunTolerant(exe, $"s {LocalDbInstance}", 60);

        report("در حال ساخت پایگاه داده «" + database + "»...");
        var master = $@"Data Source=(localdb)\{LocalDbInstance};Initial Catalog=master;" +
                     "Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=30";
        using (var con = new SqlConnection(master))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{database.Replace("'", "''")}') IS NULL CREATE DATABASE [{database.Replace("]", "]]")}]";
            cmd.CommandTimeout = 0;
            cmd.ExecuteNonQuery();
        }

        // Final connectivity check exactly as the app will connect.
        var appCs = $@"Data Source=(localdb)\{LocalDbInstance};Initial Catalog={database};" +
                    "Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=30";
        using (var con = new SqlConnection(appCs))
        {
            con.Open();
        }
        SetupLog.Info($"LocalDB + database '{database}' verified");
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
