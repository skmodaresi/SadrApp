using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

/// <summary>Backup and restore of the current database via T-SQL BACKUP/RESTORE DATABASE.</summary>
public partial class BackupWindow : Window
{
    private string _dbPath = "";

    public BackupWindow()
    {
        InitializeComponent();
        BtnBackup.Click += (_, _) => Backup();
        BtnRestore.Click += (_, _) => Restore();
        BtnOpenFolder.Click += (_, _) => OpenFolder();
        Loaded += (_, _) => LoadDbInfo();
    }

    private void LoadDbInfo()
    {
        try
        {
            var dt = Database.Query("SELECT DB_NAME() AS Db, CAST(SERVERPROPERTY('Edition') AS nvarchar(100)) AS Ed");
            _dbPath = Database.Scalar(
                "SELECT CONVERT(nvarchar(4000), physical_name) FROM sys.database_files WHERE type_desc = 'ROWS'") as string ?? "";
            TxtFolder.Text = _dbPath;
            Log($"متصل به «{dt.Rows[0]["Db"]}» روی سرور فعلی آماده است.");
        }
        catch (Exception ex)
        {
            Log("خطا در خواندن اطلاعات بانک: " + ex.Message);
        }
    }

    private void Backup()
    {
        var db = GetDbName();
        if (db is null) return;

        var suggested = $"SadrBackup-{db}-{DateTime.Now:yyyy-MM-dd_HH-mm}.bak";
        var dlg = new SaveFileDialog
        {
            Title = "ذخیره نسخه پشتیبان",
            Filter = "نسخه پشتیبان (*.bak)|*.bak",
            FileName = suggested,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != true) return;
        var target = dlg.FileName;

        if (!string.IsNullOrEmpty(_dbPath))
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Log("پوشه داده سرور یافت نشد؛ از مسیرهای محلی استفاده کنید.");
                return;
            }
        }

        BtnBackup.IsEnabled = false;
        Log($"در حال تهیه نسخه پشتیبان از «{db}» ...");
        try
        {
            var temp = target + ".sadr-temp";
            try
            {
                // SQL Server's service account writes the backup file itself, so it may not be
                // able to write into the chosen folder. Try in place first; if the service is
                // denied, stage the file in %PUBLIC%\Documents (service-writable, user-readable)
                // and copy it to the chosen location.
                var fileName = Path.GetFileName(target);
                var staged = Path.Combine(Path.GetDirectoryName(target) ?? Path.GetTempPath(), fileName);
                var usedPublic = false;
                try
                {
                    RunBackup(db, staged);
                }
                catch (SqlException)
                {
                    usedPublic = true;
                    staged = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), fileName);
                    RunBackup(db, staged);
                }
                if (usedPublic)
                {
                    File.Copy(staged, temp, overwrite: true);
                    try { File.Delete(staged); } catch { /* leftover is harmless */ }
                    File.Move(temp, target, overwrite: true);
                }
                Log($"✅ نسخه پشتیبان ذخیره شد: {target}");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? "";
            Log("❌ خطا در پشتیبان‌گیری: " + (inner.Length > 0 ? inner : ex.Message));
            MessageBox.Show((inner.Length > 0 ? inner : ex.Message), "خطا در پشتیبان‌گیری",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnBackup.IsEnabled = true;
        }
    }

    /// <summary>Runs BACKUP DATABASE with COMPRESSION, falling back to uncompressed on old Express editions.</summary>
    private static void RunBackup(string db, string path)
    {
        try
        {
            Database.Execute($"BACKUP DATABASE [{db}] TO DISK = @p WITH INIT, COMPRESSION, STATS = 25",
                new SqlParameter("@p", path));
        }
        catch (SqlException ex) when (ex.Message.Contains("compression", StringComparison.OrdinalIgnoreCase))
        {
            Database.Execute($"BACKUP DATABASE [{db}] TO DISK = @p WITH INIT, STATS = 25",
                new SqlParameter("@p", path));
        }
    }

    private void Restore()
    {
        var db = GetDbName();
        if (db is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "انتخاب فایل نسخه پشتیبان",
            Filter = "نسخه پشتیبان (*.bak)|*.bak"
        };
        if (dlg.ShowDialog(this) != true) return;
        var source = dlg.FileName;

        if (MessageBox.Show(
                $"بازیابی «{db}» از فایل انتخاب‌شده، همه اطلاعات فعلی را با محتوای فایل پشتیبان جایگزین می‌کند و بازگشت‌پذیر نیست.\n\nادامه می‌دهید؟",
                "تأیید بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // RESTORE requires the database to be unused, so it must run from a master-catalog
        // connection: the app's own connection sits inside the target db and would block it
        // ("RESTORE cannot process database ... because it is in use by this session").
        var masterCs = new SqlConnectionStringBuilder(Database.ConnectionString) { InitialCatalog = "master" }.ConnectionString;

        BtnRestore.IsEnabled = false;
        Log($"در حال بازیابی «{db}» از {source} ...");
        SqlConnection? master = null;
        var inSingleUser = false;
        try
        {
            master = new SqlConnection(masterCs);
            master.Open();

            void Exec(SqlConnection c, string sql, SqlParameter? p = null)
            {
                using var cmd = new SqlCommand(sql, c) { CommandTimeout = 3600 };
                if (p != null) cmd.Parameters.Add(p);
                cmd.ExecuteNonQuery();
            }

            // Everyone else must be out of the database before a RESTORE can take it single-user.
            Exec(master, $"ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            inSingleUser = true;

            // Try in place first; if the backup carries file paths that don't exist on this
            // machine, redo it with explicit MOVE targets derived from the backup itself.
            try
            {
                Exec(master, $"RESTORE DATABASE [{db}] FROM DISK = @p WITH REPLACE, RECOVERY, STATS = 25",
                    new SqlParameter("@p", source));
            }
            catch (SqlException)
            {
                var files = Database.Query(
                    "RESTORE FILELISTONLY FROM DISK = @p",
                    new SqlParameter("@p", source));
                var destDir = string.IsNullOrEmpty(_dbPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)
                    : Path.GetDirectoryName(_dbPath)!;
                var moves = new List<string>();
                foreach (DataRow r in files.Rows)
                {
                    var logical = r["LogicalName"].ToString()?.Replace("'", "''");
                    var ext = r["Type"].ToString() == "L" ? "_log.ldf" : ".mdf";
                    var dest = Path.Combine(destDir, $"{db}_{Guid.NewGuid():N}{ext}");
                    moves.Add($"MOVE N'{logical}' TO N'{dest.Replace("'", "''")}'");
                }
                Exec(master, $"RESTORE DATABASE [{db}] FROM DISK = @p WITH REPLACE, RECOVERY, {string.Join(", ", moves)}",
                    new SqlParameter("@p", source));
            }

            Exec(master, $"ALTER DATABASE [{db}] SET MULTI_USER");
            inSingleUser = false;
            SqlConnection.ClearAllPools(); // the app's pooled connections died with SINGLE_USER

            Log("✅ بازیابی با موفقیت انجام شد.");
            MessageBox.Show("بازیابی کامل شد. برنامه را بازخوانی کنید.", "بازیابی",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            Log("❌ خطا در بازیابی: " + msg);
            MessageBox.Show(msg, "خطا در بازیابی", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (inSingleUser)
            {
                try
                {
                    using var cmd = new SqlCommand($"ALTER DATABASE [{db}] SET MULTI_USER", master) { CommandTimeout = 60 };
                    cmd.ExecuteNonQuery();
                }
                catch { /* RESTORE with RECOVERY may have already brought it back online. */ }
            }
            master?.Dispose();
            SqlConnection.ClearAllPools();
            BtnRestore.IsEnabled = true;
        }
    }

    private void OpenFolder()
    {
        try
        {
            var dir = string.IsNullOrEmpty(_dbPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start("explorer.exe", $"\"{dir}\"");
            else
                Log("پوشه یافت نشد.");
        }
        catch (Exception ex)
        {
            Log("خطا در بازکردن پوشه: " + ex.Message);
        }
    }

    private string? GetDbName()
    {
        try { return Database.Scalar("SELECT DB_NAME()") as string; }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private void Log(string msg) =>
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
}
