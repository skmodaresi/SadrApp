using System.Windows;
using System.Windows.Controls;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.Views.Pages;

namespace SadrApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MnuPreInvoices.Click += (_, _) => OpenTab(new InvoiceListPage(InvoiceTypeConsts.PreInvoice), "پیش‌فاکتورها");
        MnuSellInvoices.Click += (_, _) => OpenTab(new InvoiceListPage(InvoiceTypeConsts.Sell), "فاکتورهای فروش");
        MnuBuyInvoices.Click += (_, _) => OpenTab(new InvoiceListPage(InvoiceTypeConsts.Buy), "فاکتورهای خرید");
        MnuAllInvoices.Click += (_, _) => OpenTab(new InvoiceListPage(null), "همه فاکتورها");
        MnuAccountGroups.Click += (_, _) => OpenTab(new AccountingPage(AccountingPage.Level.Groups, "گروه‌های حساب"), "گروه‌های حساب");
        MnuGeneralAccounts.Click += (_, _) => OpenTab(new AccountingPage(AccountingPage.Level.Generals, "حساب‌های کل"), "حساب‌های کل");
        MnuSubSidiaryAccounts.Click += (_, _) => OpenTab(new AccountingPage(AccountingPage.Level.Subsidiaries, "حساب‌های معین"), "حساب‌های معین");
        MnuDetailAccounts.Click += (_, _) => OpenTab(new AccountingPage(AccountingPage.Level.Details, "حساب‌های تفصیلی"), "حساب‌های تفصیلی");
        MnuProductCategories.Click += (_, _) => OpenTab(new CategoryTreePage(true, "مدیریت دسته‌بندی کالا"), "دسته‌بندی کالا");
        MnuProducts.Click += (_, _) => OpenTab(new ProductListPage(), "کالاها");
        MnuBrands.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Brands, "برندها"), "برندها");
        MnuUnits.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Units, "واحدهای شمارش"), "واحدهای شمارش");
        MnuAttributes.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Attributes, "ویژگی‌های کالا"), "ویژگی‌های کالا");
        MnuProjectCategories.Click += (_, _) => OpenTab(new CategoryTreePage(false, "مدیریت دسته‌بندی پروژه"), "دسته‌بندی پروژه");
        MnuProjects.Click += (_, _) => OpenTab(new ProjectListPage(), "پروژه‌ها");
        MnuTasks.Click += (_, _) => OpenTasksForProject(null);
        MnuPeople.Click += (_, _) => OpenTab(new PeopleListPage(), "اشخاص");
        MnuCompanies.Click += (_, _) => OpenTab(new CompanyListPage(), "شرکت‌ها");
        MnuCustomersProviders.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.CustomersProviders, "مشتریان و تأمین‌کنندگان"), "مشتریان و تأمین‌کنندگان");
        MnuUsers.Click += (_, _) => OpenTab(new UserListPage(), "کاربران برنامه");
        MnuBanks.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Banks, "بانک‌ها"), "بانک‌ها");
        MnuBankBranches.Click += (_, _) => OpenTab(new BankPage(), "شعب بانک‌ها");
        MnuWarehouses.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Warehouses, "انبارها"), "انبارها");
        MnuCurrencies.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Currencies, "واحدهای پول"), "واحدهای پول");
        MnuRoles.Click += (_, _) => OpenTab(new SimpleListPage(SimpleListPage.Kind.Roles, "نقش‌ها"), "نقش‌ها");
        MnuConnection.Click += (_, _) => ShowConnectionSettings();
        MnuPrintSettings.Click += (_, _) => new SadrApp.Views.PrintSettingsWindow { Owner = this }.ShowDialog();
        MnuExit.Click += (_, _) => Close();
        Loaded += (_, _) => ShowConnectionSummary();
    }

    /// <summary>Opens (or re-selects) the tasks tab, optionally filtered to one project.</summary>
    public void OpenTasksForProject(int? projectId)
    {
        var title = projectId is int pid ? $"وظایف پروژه #{pid}" : "وظایف پروژه‌ها";
        foreach (TabItem existing in Tabs.Items)
        {
            if ((string)existing.Tag == title) { existing.IsSelected = true; return; }
        }
        OpenTab(new TaskListPage(projectId), title);
    }

    private void OpenTab(UserControl page, string title)
    {
        foreach (TabItem existing in Tabs.Items)
        {
            if ((string)existing.Tag == title)
            {
                existing.Content = page;
                existing.IsSelected = true;
                return;
            }
        }
        var tab = new TabItem { Content = page, Tag = title };
        tab.Header = MakeTabHeader(title, tab);
        Tabs.Items.Add(tab);
        tab.IsSelected = true;
    }

    private object MakeTabHeader(string title, TabItem owner)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button
        {
            Content = "✕",
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.Gray
        };
        close.Click += (_, _) => Tabs.Items.Remove(owner);
        panel.Children.Add(close);
        return panel;
    }

    private void ShowConnectionSettings()
    {
        var dlg = new ConnectionSettingsWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            ShowConnectionSummary();
            StatusText.Text = "اتصال به‌روزرسانی شد — صفحه‌ها را بازخوانی کنید.";
        }
    }

    private void ShowConnectionSummary()
    {
        try
        {
            var cs = Database.ConnectionString;
            var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var server = parts.FirstOrDefault(p => p.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase));
            var db = parts.FirstOrDefault(p => p.StartsWith("Initial Catalog", StringComparison.OrdinalIgnoreCase));
            var auth = cs.Contains("Integrated Security", StringComparison.OrdinalIgnoreCase) ? "Windows Auth" : "SQL Auth";
            ConnText.Text = $"سرور: {server?["Data Source=".Length..]} | پایگاه داده: {db?["Initial Catalog=".Length..]} | {auth} | فایل: Connection.dat";
        }
        catch
        {
            ConnText.Text = "رشته اتصال معتبر نیست — از منوی سیستم تنظیمات را ویرایش کنید.";
        }
    }
}
