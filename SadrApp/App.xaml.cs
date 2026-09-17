using System.Windows;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.Views;

namespace SadrApp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Force right-to-left flow direction for the whole application.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("fa-IR")));

        // A single UI error must never close the whole application.
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "خطای غیرمنتظره", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        base.OnStartup(e);

        // First-run: make sure a Connection.dat exists next to the exe.
        if (!DbConfig.Exists())
            DbConfig.Save(Database.ConnectionString);

        // Try to reach the database; allow the user to fix settings.
        while (!Database.TestConnection(Database.ConnectionString, out var error))
        {
            var dlg = new StartupDialog(error);
            if (dlg.ShowDialog() == true)
            {
                var settings = new ConnectionSettingsWindow();
                if (settings.ShowDialog() == true) continue;
            }
            Shutdown();
            return;
        }

        try
        {
            DbBootstrapper.EnsureDatabase(); // create DB + EF schema when missing (first run)
            SeedData.Run();
            await using (var db = SadrDb.New())
            {
                PrintSettingsService.EnsureTableAndSeed(db);   // invoice print layouts table + 3 defaults
                await CustomersProviderService.BackfillAsync(db); // mirror rows for existing people/companies
                TaskService.EnsureSchema(db);                  // TaskReports.FreeTaskId FK must not block project-task reports
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("خطا در ایجاد داده‌های پیش‌فرض:\n" + ex.Message, "هشدار",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // First-run wizard: when no user exists yet, ask for the main admin credentials.
        try
        {
            if (!DbBootstrapper.AdminUserExists())
            {
                var setup = new FirstRunSetupWindow();
                if (setup.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("خطا در بررسی کاربر مدیر:\n" + ex.Message, "هشدار",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Login gate — SeedData guarantees at least the default admin user exists.
        var login = new LoginWindow();
        if (login.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
