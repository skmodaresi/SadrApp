using System.ComponentModel;
using System.Diagnostics;

namespace SadrSetup;

/// <summary>
/// Setup wizard UI: detects an existing installation (update mode keeps Connection.dat
/// and only copies changed files) or walks through a fresh install — pick a SQL Server
/// engine, log in (Windows or SQL auth), then choose an existing database or create a new one.
/// </summary>
public class MainForm : Form
{
    private readonly TextBox _txtPath = new();
    private readonly Button _btnBrowse = new();
    private readonly Label _lblMode = new();

    private readonly GroupBox _grpDb = new();
    private readonly RadioButton _rbLocalDb = new();
    private readonly RadioButton _rbExisting = new();
    private readonly ComboBox _cmbServer = new();
    private readonly Button _btnRefresh = new();
    private readonly RadioButton _rbAuthWin = new();
    private readonly RadioButton _rbAuthSql = new();
    private readonly Label _lblAuth = new();
    private readonly Label _lblUser = new();
    private readonly Label _lblPass = new();
    private readonly TextBox _txtUser = new();
    private readonly TextBox _txtPass = new();
    private readonly Button _btnConnect = new();
    private readonly ComboBox _cmbDb = new();
    private readonly Label _lblDbHint = new();

    private readonly Button _btnInstall = new();
    private readonly ProgressBar _bar = new();
    private readonly TextBox _txtLog = new();

    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string PayloadDir = Path.Combine(BaseDir, "app");

    public MainForm()
    {
        Text = "نصب و به‌روزرسانی سامانه حسابداری و مدیریت پروژه — صدر";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(600, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // ---- install folder -------------------------------------------------
        var lbl1 = new Label { Text = "پوشه نصب:", Location = new Point(20, 18), AutoSize = true };
        _txtPath.SetBounds(20, 42, 400, 26);
        _btnBrowse.Text = "...";
        _btnBrowse.SetBounds(430, 41, 40, 26);
        _btnBrowse.Click += (_, _) => Browse();
        _txtPath.TextChanged += (_, _) => EvaluateMode();

        _lblMode.SetBounds(20, 74, 560, 22);
        _lblMode.AutoSize = false;
        _lblMode.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        // ---- database group -------------------------------------------------
        _grpDb.Text = "پایگاه داده";
        _grpDb.SetBounds(20, 102, 560, 248);

        _rbLocalDb.Text = "نصب خودکار SQL Server LocalDB (رایگان، نیازی به سرور ندارد)";
        _rbLocalDb.SetBounds(15, 24, 530, 24);
        _rbLocalDb.Checked = true;
        _rbLocalDb.CheckedChanged += (_, _) => DbModeChanged();

        _rbExisting.Text = "اتصال به SQL Server موجود:";
        _rbExisting.SetBounds(15, 54, 215, 24);
        _rbExisting.CheckedChanged += (_, _) => DbModeChanged();

        _cmbServer.SetBounds(235, 52, 270, 26);
        _cmbServer.DropDownStyle = ComboBoxStyle.DropDown; // editable: manual entry allowed
        _btnRefresh.Text = "↻";
        _btnRefresh.SetBounds(510, 52, 35, 26);
        _btnRefresh.Click += (_, _) => LoadServers();

        _lblAuth.Text = "روش ورود:";
        _lblAuth.Location = new Point(15, 86);
        _lblAuth.AutoSize = true;
        _rbAuthWin.Text = "احراز هویت ویندوز";
        _rbAuthWin.SetBounds(105, 85, 170, 24);
        _rbAuthWin.Checked = true;
        _rbAuthWin.CheckedChanged += (_, _) => AuthChanged();
        _rbAuthSql.Text = "نام کاربری و رمز عبور SQL Server";
        _rbAuthSql.SetBounds(280, 85, 250, 24);
        _rbAuthSql.CheckedChanged += (_, _) => AuthChanged();

        _lblUser.Text = "نام کاربری:";
        _lblUser.Location = new Point(15, 117);
        _lblUser.AutoSize = true;
        _txtUser.SetBounds(105, 114, 170, 26);
        _lblPass.Text = "رمز عبور:";
        _lblPass.Location = new Point(285, 117);
        _lblPass.AutoSize = true;
        _txtPass.SetBounds(360, 114, 175, 26);
        _txtPass.UseSystemPasswordChar = true;

        _btnConnect.Text = "اتصال و نمایش پایگاه‌ها";
        _btnConnect.SetBounds(15, 146, 220, 30);
        _btnConnect.Click += (_, _) => ConnectAndListDatabases();

        var lblDb = new Label { Text = "پایگاه داده:", Location = new Point(15, 190), AutoSize = true };
        _cmbDb.SetBounds(105, 187, 270, 26);
        _cmbDb.DropDownStyle = ComboBoxStyle.DropDown; // pick existing or type a new name
        _cmbDb.Text = "SadrApp";
        _lblDbHint.SetBounds(105, 216, 430, 22);
        _lblDbHint.AutoSize = false;
        _lblDbHint.Font = new Font("Segoe UI", 8.5f);
        _lblDbHint.ForeColor = Color.Gray;
        _lblDbHint.Text = "برای ساخت پایگاه جدید، نام دلخواه را بنویسید و نصب را شروع کنید.";

        _grpDb.Controls.AddRange(new Control[]
        {
            _rbLocalDb, _rbExisting, _cmbServer, _btnRefresh,
            _lblAuth, _rbAuthWin, _rbAuthSql,
            _lblUser, _txtUser, _lblPass, _txtPass,
            _btnConnect, lblDb, _cmbDb, _lblDbHint
        });

        // ---- actions --------------------------------------------------------
        _btnInstall.Text = "شروع نصب";
        _btnInstall.SetBounds(20, 362, 170, 36);
        _btnInstall.Click += (_, _) => RunInstall();

        _bar.SetBounds(20, 406, 560, 22);

        _txtLog.SetBounds(20, 436, 560, 170);
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.Font = new Font("Consolas", 9f);

        Controls.AddRange(new Control[]
        {
            lbl1, _txtPath, _btnBrowse, _lblMode, _grpDb, _btnInstall, _bar, _txtLog
        });

        // ---- startup: preselect an existing installation if one is found -----
        var existing = Steps.FindExistingInstall();
        _txtPath.Text = string.IsNullOrEmpty(existing)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SadrApp")
            : existing;
        LoadServers();
        DbModeChanged();
    }

    // ------------------------------------------------------------------
    // mode / option handling
    // ------------------------------------------------------------------

    /// <summary>Update mode when the selected folder already contains the app.</summary>
    private bool IsUpdateMode =>
        File.Exists(Path.Combine(_txtPath.Text.Trim(), "SadrApp.exe"));

    private void EvaluateMode()
    {
        var update = IsUpdateMode;
        _lblMode.Text = update
            ? "حالت: به‌روزرسانی نسخهٔ نصب‌شده — پایگاه داده و Connection.dat تغییر نمی‌کنند"
            : "حالت: نصب جدید";
        _lblMode.ForeColor = update ? Color.FromArgb(0x1F, 0x6F, 0x3D) : Color.Black;
        _grpDb.Enabled = !update;
        _btnInstall.Text = update ? "شروع به‌روزرسانی" : "شروع نصب";
    }

    private void DbModeChanged()
    {
        var existing = _rbExisting.Checked;
        _cmbServer.Enabled = existing;
        _btnRefresh.Enabled = existing;
        AuthChanged();
    }

    private void AuthChanged()
    {
        // auth options only apply to an existing server; LocalDB always uses Windows auth
        var existing = _rbExisting.Checked;
        _rbAuthWin.Enabled = existing;
        _rbAuthSql.Enabled = existing;
        var sqlAuth = _rbAuthSql.Checked && existing;
        _txtUser.Enabled = sqlAuth;
        _txtPass.Enabled = sqlAuth;
        _lblAuth.Enabled = existing;
        _lblUser.Enabled = existing;
        _lblPass.Enabled = existing;
        _btnConnect.Enabled = existing;
    }

    private void LoadServers()
    {
        try
        {
            var servers = Steps.DiscoverSqlServerInstances();
            if (Steps.LocalDbInstalled())
                servers.Add(@"(localdb)\" + Steps.LocalDbInstance);
            _cmbServer.Items.Clear();
            foreach (var s in servers) _cmbServer.Items.Add(s);
            if (_cmbServer.Items.Count > 0 && _cmbServer.Text.Length == 0)
                _cmbServer.SelectedIndex = 0;
            if (_cmbServer.Items.Count == 0)
                Report("هیچ SQL Server روی این سیستم شناسایی نشد — نام سرور را دستی وارد کنید.");
        }
        catch (Exception ex)
        {
            SetupLog.Error("LoadServers: " + ex.Message);
        }
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _txtPath.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtPath.Text = dlg.SelectedPath;
    }

    private void Report(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Report(msg)); return; }
        _txtLog.AppendText(msg + Environment.NewLine);
        SetupLog.Info(msg);
    }

    // ------------------------------------------------------------------
    // connect step (lists existing databases from the chosen server)
    // ------------------------------------------------------------------

    private void ConnectAndListDatabases()
    {
        var server = _cmbServer.Text.Trim();
        if (server.Length == 0)
        {
            MessageBox.Show("ابتدا یک سرور SQL انتخاب یا وارد کنید.", "سرور",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var winAuth = _rbAuthWin.Checked;
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text;
        _btnConnect.Enabled = false;
        _lblDbHint.Text = "در حال اتصال…";

        var cs = Steps.BuildConnectionString(server, "master", winAuth, user, pass);
        var worker = new BackgroundWorker();
        worker.DoWork += (_, args) =>
        {
            if (!Steps.TryConnect(cs, out var error))
                throw new InvalidOperationException(error);
            args.Result = Steps.ListUserDatabases(cs);
        };
        worker.RunWorkerCompleted += (_, args) =>
        {
            _btnConnect.Enabled = true;
            if (args.Error is not null)
            {
                _lblDbHint.Text = "اتصال برقرار نشد — خطا در setup.log ثبت شد.";
                SetupLog.Error("connect: " + args.Error.Message);
                MessageBox.Show("اتصال به سرور برقرار نشد:\n" + args.Error.Message,
                    "خطا در اتصال", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var dbs = (List<string>)args.Result!;
            _cmbDb.Items.Clear();
            foreach (var d in dbs) _cmbDb.Items.Add(d);
            _lblDbHint.Text = dbs.Count > 0
                ? "فهرست پایگاه‌های موجود بارگذاری شد — یکی را انتخاب کنید یا نام جدید بنویسید."
                : "پایگاهی یافت نشد — نام پایگاه جدید را بنویسید.";
            if (_cmbDb.Text.Trim().Length == 0)
                _cmbDb.Text = dbs.Count > 0 ? dbs[0] : "SadrApp";
            Report($"اتصال برقرار شد؛ {dbs.Count} پایگاه داده یافت شد.");
        };
        worker.RunWorkerAsync();
    }

    // ------------------------------------------------------------------
    // install / update
    // ------------------------------------------------------------------

    private void RunInstall()
    {
        var installDir = _txtPath.Text.Trim();
        if (installDir.Length == 0)
        {
            MessageBox.Show("پوشه نصب را انتخاب کنید.", "پوشه نصب",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var updateMode = IsUpdateMode;
        var useLocalDb = _rbLocalDb.Checked;
        var server = _cmbServer.Text.Trim();
        var database = _cmbDb.Text.Trim();
        var winAuth = _rbAuthWin.Checked;
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text;

        if (!updateMode)
        {
            if (!useLocalDb && server.Length == 0)
            {
                MessageBox.Show("یک سرور SQL Server انتخاب یا وارد کنید.", "سرور",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!useLocalDb && !winAuth && user.Length == 0)
            {
                MessageBox.Show("نام کاربری SQL Server را وارد کنید.", "ورود SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (database.Length == 0)
            {
                MessageBox.Show("نام پایگاه داده را وارد کنید (یا از فهرست انتخاب کنید).", "پایگاه داده",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _btnInstall.Enabled = false;
        var worker = new BackgroundWorker();
        worker.DoWork += (_, _) =>
            InstallAll(installDir, updateMode, useLocalDb, server, database, winAuth, user, pass);
        worker.RunWorkerCompleted += (_, args) =>
        {
            _btnInstall.Enabled = true;
            if (args.Error is null)
            {
                _bar.Value = 100;
                MessageBox.Show(
                    updateMode
                        ? "به‌روزرسانی کامل شد. تنظیمات اتصال (Connection.dat) حفظ شد."
                        : "نصب کامل شد.\nاولین اجرای برنامه از شما نام کاربری و رمز عبور مدیر را می‌پرسد.",
                    updateMode ? "به‌روزرسانی" : "نصب",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                AskLaunch(Path.Combine(installDir, "SadrApp.exe"));
                Close();
            }
            else
            {
                MessageBox.Show("خطا در نصب:\n" + args.Error.Message +
                                "\nجزئیات: " + SetupLog.LogPath,
                    "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        worker.RunWorkerAsync();
    }

    private void InstallAll(string installDir, bool updateMode, bool useLocalDb,
        string server, string database, bool winAuth, string user, string password)
    {
        try
        {
            // 1) .NET runtime (both modes — a newer release may need a newer runtime)
            if (!Steps.DotNetInstalled())
            {
                var dotnetExe = Path.Combine(BaseDir, "redist", "windowsdesktop-runtime-10.0-win-x64.exe");
                if (!File.Exists(dotnetExe))
                    throw new FileNotFoundException("پکیج .NET یافت نشد: " + dotnetExe);
                Steps.InstallDotNet(dotnetExe, Report);
            }
            else Report(".NET Desktop Runtime از قبل نصب است.");

            // 2) update mode: preserve Connection.dat, only refresh changed files
            if (updateMode)
            {
                var connDat = Path.Combine(installDir, "Connection.dat");
                if (File.Exists(connDat))
                {
                    var info = Steps.ReadConnectionInfo(connDat);
                    Report($"فایل Connection.dat حفظ شد — سرور: {info[0]} | پایگاه داده: {info[1]} | ورود: {info[2]}");
                }
                else Report("Connection.dat در پوشه نصب یافت نشد (حالت به‌روزرسانی).");

                CopyPayloadAndFinish(installDir);
                Report("به‌روزرسانی با موفقیت انجام شد.");
                return;
            }

            // 3) fresh install: provision the database engine + database
            string connServer;
            bool connWinAuth;
            string connUser, connPass;
            if (useLocalDb)
            {
                connServer = @"(localdb)\" + Steps.LocalDbInstance;
                connWinAuth = true; // LocalDB has Windows auth only
                connUser = connPass = "";
                if (!Steps.LocalDbInstalled())
                {
                    var msi = Path.Combine(BaseDir, "redist", "SqlLocalDB-2022.msi");
                    if (!File.Exists(msi))
                        throw new FileNotFoundException("پکیج LocalDB یافت نشد: " + msi);
                    Steps.InstallLocalDb(msi, Report);
                }
                else Report("SQL Server LocalDB از قبل نصب است.");
                Steps.EnsureLocalDbRunning(Report);
                Steps.EnsureDatabase(connServer, database, true, "", "", Report);
            }
            else
            {
                connServer = server;
                connWinAuth = winAuth;
                connUser = user;
                connPass = password;
                // verify the login the user provided before anything else
                var masterCs = Steps.BuildConnectionString(server, "master", winAuth, user, password);
                if (!Steps.TryConnect(masterCs, out var error))
                    throw new InvalidOperationException("اتصال به سرور برقرار نشد:\n" + error);
                Report("اتصال به سرور تأیید شد.");
                Steps.EnsureDatabase(server, database, winAuth, user, password, Report);
            }

            // 4) payload + Connection.dat + shortcut
            CopyPayloadAndFinish(installDir);
            Steps.WriteConnectionDat(installDir,
                Steps.BuildConnectionString(connServer, database, connWinAuth, connUser, connPass));

            Report("نصب با موفقیت انجام شد.");
        }
        catch (Exception ex)
        {
            SetupLog.Error(ex.ToString());
            throw;
        }
    }

    private void CopyPayloadAndFinish(string installDir)
    {
        if (!Directory.Exists(PayloadDir))
            throw new DirectoryNotFoundException("پوشه app در کنار نصب‌کننده یافت نشد.");
        Steps.CopyPayload(PayloadDir, installDir, Report);
        Steps.RegisterInstallLocation(installDir);
        Steps.CreateShortcut(Path.Combine(installDir, "SadrApp.exe"));
    }

    private void AskLaunch(string exe)
    {
        if (File.Exists(exe) && MessageBox.Show("برنامه اجرا شود؟", "نصب",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }
}
