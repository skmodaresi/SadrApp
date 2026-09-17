using System.Windows;
using System.Windows.Controls;
using SadrApp.Infrastructure;

namespace SadrApp.Views;

public partial class ConnectionSettingsWindow : Window
{
    public ConnectionSettingsWindow()
    {
        InitializeComponent();
        ServerBox.TextChanged += (_, _) => Rebuild();
        DbBox.TextChanged += (_, _) => Rebuild();
        AuthSql.Checked += (_, _) => Rebuild();
        AuthWindows.Checked += (_, _) => Rebuild();
        UserBox.TextChanged += (_, _) => Rebuild();
        PassBox.PasswordChanged += (_, _) => Rebuild();

        FinalBox.TextChanged += (_, e) =>
        {
            if (_updating) return;
            // user edited the raw string; stop rebuilding while typing
            _manual = true;
        };

        LoadCurrent();
        BtnTest.Click += (_, _) => Test();
        BtnSave.Click += (_, _) => Save();
    }

    private bool _updating;
    private bool _manual;

    private void LoadCurrent()
    {
        var cs = Database.ConnectionString;
        FinalBox.Text = cs;
        var parts = ParseConnectionString(cs);
        ServerBox.Text = parts.GetValueOrDefault("Data Source", "");
        DbBox.Text = parts.GetValueOrDefault("Initial Catalog", "");
        var integrated = parts.GetValueOrDefault("Integrated Security", "").Equals("true", StringComparison.OrdinalIgnoreCase)
                         || parts.GetValueOrDefault("Integrated Security", "") == "SSPI";
        AuthWindows.IsChecked = integrated;
        AuthSql.IsChecked = !integrated;
        UserBox.Text = parts.GetValueOrDefault("User ID", "");
        PassBox.Password = parts.GetValueOrDefault("Password", "");
        _manual = false;
    }

    private static Dictionary<string, string> ParseConnectionString(string cs)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }

    private void Rebuild()
    {
        if (_manual) return;
        _updating = true;
        var server = ServerBox.Text.Trim();
        var db = DbBox.Text.Trim();
        var sb = new List<string>();
        if (server.Length > 0) sb.Add($"Data Source={server}");
        if (db.Length > 0) sb.Add($"Initial Catalog={db}");
        if (AuthWindows.IsChecked == true)
        {
            sb.Add("Integrated Security=True");
        }
        else
        {
            sb.Add("Persist Security Info=True");
            sb.Add($"User ID={UserBox.Text.Trim()}");
            sb.Add($"Password={PassBox.Password}");
        }
        sb.Add("Pooling=False");
        sb.Add("MultipleActiveResultSets=False");
        sb.Add("Encrypt=True");
        sb.Add("TrustServerCertificate=True");
        sb.Add("Command Timeout=0");
        sb.Add($"Application Name=SadrApp");
        FinalBox.Text = string.Join(";", sb);
        _updating = false;
    }

    private void Test()
    {
        TestResult.Text = "در حال آزمایش اتصال…";
        var cs = FinalBox.Text.Trim();
        var ok = Database.TestConnection(cs, out var error);
        if (ok)
        {
            TestResult.Text = "✅ اتصال برقرار است.";
            TestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x8A, 0x3D));
        }
        else
        {
            TestResult.Text = "❌ اتصال ناموفق: " + error;
            TestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
        }
    }

    private void Save()
    {
        try
        {
            var cs = FinalBox.Text.Trim();
            if (!Database.TestConnection(cs, out var error))
            {
                var res = MessageBox.Show("اتصال با این تنظیمات برقرار نشد. آیا ذخیره شود؟\n\n" + error,
                    "اتصال ناموفق", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }
            DbConfig.Save(cs);
            Database.Reset();
            MessageBox.Show("تنظیمات ذخیره شد. فایل Connection.dat در کنار برنامه به‌روزرسانی شد.",
                "ذخیره شد", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
