using System.ComponentModel;
using System.Diagnostics;

namespace SadrSetup;

/// <summary>Setup wizard UI and orchestration of all steps.</summary>
public class MainForm : Form
{
    private readonly TextBox _txtPath = new();
    private readonly Button _btnBrowse = new();
    private readonly RadioButton _rbLocalDb = new();
    private readonly RadioButton _rbExisting = new();
    private readonly TextBox _txtServer = new();
    private readonly TextBox _txtDb = new();
    private readonly TextBox _txtLog = new();
    private readonly Button _btnInstall = new();
    private readonly ProgressBar _bar = new();

    private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string PayloadDir = Path.Combine(BaseDir, "app");

    public MainForm()
    {
        Text = "نصب سامانه حسابداری و مدیریت پروژه — صدر";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(560, 470);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var lbl1 = new Label { Text = "پوشه نصب:", Location = new Point(20, 18), AutoSize = true };
        _txtPath.SetBounds(20, 42, 400, 26);
        _txtPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SadrApp");
        _btnBrowse.Text = "...";
        _btnBrowse.SetBounds(430, 41, 40, 26);
        _btnBrowse.Click += (_, _) => Browse();

        var grp = new GroupBox { Text = "پایگاه داده" };
        grp.SetBounds(20, 84, 520, 116);
        _rbLocalDb.Text = "نصب خودکار SQL Server LocalDB (رایگان، نیازی به سرور ندارد)";
        _rbLocalDb.SetBounds(15, 25, 490, 24);
        _rbLocalDb.Checked = true;
        _rbExisting.Text = "اتصال به SQL Server موجود:";
        _rbExisting.SetBounds(15, 55, 220, 24);
        _txtServer.SetBounds(240, 53, 260, 26);
        _txtServer.Text = "MY-SERVER\\SQLExpress";
        var lblDb = new Label { Text = "نام پایگاه داده:", Location = new Point(15, 86), AutoSize = true };
        _txtDb.SetBounds(240, 83, 260, 26);
        _txtDb.Text = "SadrApp";
        grp.Controls.AddRange(new Control[] { _rbLocalDb, _rbExisting, _txtServer, lblDb, _txtDb });

        _btnInstall.Text = "شروع نصب";
        _btnInstall.SetBounds(20, 214, 150, 36);
        _btnInstall.Click += (_, _) => RunInstall();

        _bar.SetBounds(20, 262, 520, 22);

        _txtLog.SetBounds(20, 294, 520, 160);
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.Font = new Font("Consolas", 9f);

        Controls.AddRange(new Control[]
        {
            lbl1, _txtPath, _btnBrowse, grp, _btnInstall, _bar, _txtLog
        });
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

    private void RunInstall()
    {
        _btnInstall.Enabled = false;
        var installDir = _txtPath.Text.Trim();
        var useLocalDb = _rbLocalDb.Checked;
        var server = _txtServer.Text.Trim();
        var database = _txtDb.Text.Trim();

        var worker = new BackgroundWorker();
        worker.DoWork += (_, _) => InstallAll(installDir, useLocalDb, server, database);
        worker.RunWorkerCompleted += (_, args) =>
        {
            _btnInstall.Enabled = true;
            if (args.Error is null)
            {
                _bar.Value = 100;
                MessageBox.Show(
                    "نصب کامل شد.\nاولین اجرای برنامه از شما نام کاربری و رمز عبور مدیر را می‌پرسد.",
                    "نصب", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private void InstallAll(string installDir, bool useLocalDb, string server, string database)
    {
        try
        {
            // 1) .NET runtime
            if (!Steps.DotNetInstalled())
            {
                var dotnetExe = Path.Combine(BaseDir, "redist", "windowsdesktop-runtime-10.0-win-x64.exe");
                if (!File.Exists(dotnetExe))
                    throw new FileNotFoundException("پکیج .NET یافت نشد: " + dotnetExe);
                Steps.InstallDotNet(dotnetExe, Report);
            }
            else Report(".NET Desktop Runtime از قبل نصب است.");

            // 2) LocalDB (only when chosen and missing)
            if (useLocalDb)
            {
                if (!Steps.LocalDbInstalled())
                {
                    var msi = Path.Combine(BaseDir, "redist", "SqlLocalDB-2022.msi");
                    if (!File.Exists(msi))
                        throw new FileNotFoundException("پکیج LocalDB یافت نشد: " + msi);
                    Steps.InstallLocalDb(msi, Report);
                }
                else Report("SQL Server LocalDB از قبل نصب است.");

                Steps.CreateDatabase(database, Report);
            }

            // 3) Payload
            if (!Directory.Exists(PayloadDir))
                throw new DirectoryNotFoundException("پوشه app در کنار نصب‌کننده یافت نشد.");
            Steps.CopyPayload(PayloadDir, installDir, Report);

            // 4) Connection.dat (LocalDB = trusted connection, no password stored)
            if (useLocalDb)
                Steps.WriteConnectionDat(installDir, $@"(localdb)\{Steps.LocalDbInstance}", database);
            else
                Steps.WriteConnectionDat(installDir, server, database);

            // 5) Shortcut
            Steps.CreateShortcut(Path.Combine(installDir, "SadrApp.exe"));

            Report("نصب با موفقیت انجام شد.");
        }
        catch (Exception ex)
        {
            SetupLog.Error(ex.ToString());
            throw;
        }
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
