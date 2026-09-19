using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Management page for the BankAccounts table: list, filter by bank, and CRUD via the shared field editor.</summary>
public partial class BankAccountsPage : UserControl
{
    /// <summary>Cache of BankBranchId → "bank - branch" display names for the grid and the editor.</summary>
    private Dictionary<int, string> _branchNames = new();

    /// <summary>Id of the account whose statement is currently shown, to avoid reload loops.</summary>
    private int? _stmtAccountId;

    public BankAccountsPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => { LoadBanks(); Load(); };
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        ListCtl.InnerGrid.SelectionChanged += (_, _) =>
        {
            if (ListCtl.SelectedRow is RowBase r) ShowStatement(r.Id);
        };
        BankFilter.SelectionChanged += (_, _) => Load();
        Loaded += (_, _) => { LoadBanks(); Load(); };
    }

    private async void LoadBanks()
    {
        try
        {
            await using var db = SadrDb.New();
            var banks = await db.Banks.Where(b => !b.Deleted)
                .Select(b => new ComboItem { Key = b.Id, Text = b.Name }).ToListAsync();
            banks.Insert(0, new ComboItem { Key = 0, Text = "— همه بانک‌ها —" });
            BankFilter.ItemsSource = banks;
            BankFilter.DisplayMemberPath = "Text";
            BankFilter.SelectedValuePath = "Key";
            if (BankFilter.SelectedValue is null) BankFilter.SelectedValue = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            _branchNames = await db.BankBranches.Where(b => !b.Deleted)
                .Select(b => new { b.Id, Name = b.Bank!.Name + " - " + b.Name })
                .ToDictionaryAsync(b => b.Id, b => b.Name);

            var bankId = BankFilter.SelectedValue as int?;
            var query = db.BankAccounts.Where(a => !a.Deleted);
            if (bankId is > 0)
            {
                var branchIds = await db.BankBranches.Where(b => !b.Deleted && b.BankId == bankId)
                    .Select(b => b.Id).ToListAsync();
                query = query.Where(a => branchIds.Contains(a.BankBranchId));
            }

            var rows = await query
                .Select(a => new
                {
                    a.Id, a.Name, a.AccountNumber, a.BankBranchId,
                    Currency = a.Currency.Name,
                    Owner = a.PersonId != null ? a.Person.FirstName + " " + a.Person.LastName
                          : a.CompanyId != null ? a.Company.FullName : "",
                    a.CardNumber, a.AccountType, a.StartBalance, a.CurrentBalance, a.Description
                })
                .ToListAsync();

            var typeNames = new[] { "جاری", "پس‌انداز", "سپرده کوتاه‌مدت", "سپرده بلندمدت" };
            ListCtl.SetRows(rows.Select(a => new RowBase
            {
                Id = a.Id,
                Title = a.Name,
                Code = a.AccountNumber,
                Extra = new[]
                {
                    _branchNames.TryGetValue(a.BankBranchId, out var bn) ? bn : "شعبه " + a.BankBranchId,
                    a.Currency,
                    a.AccountType >= 0 && a.AccountType < typeNames.Length ? typeNames[a.AccountType] : "",
                    "موجودی: " + a.CurrentBalance.ToString("N0")
                }.Where(s => s.Length > 0).Aggregate((x, y) => x + " | " + y),
                Description = new[]
                {
                    a.Owner.Length > 0 ? "مالک: " + a.Owner : "",
                    !string.IsNullOrEmpty(a.CardNumber) ? "کارت: " + a.CardNumber : "",
                    a.Description ?? ""
                }.Where(s => s.Length > 0).Aggregate((x, y) => x + " | " + y)
            }).ToList());
            ListCtl.ShowExtraColumn("شعبه | واحد پول | نوع | موجودی");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک حساب را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var branches = await db.BankBranches.Where(b => !b.Deleted)
                .Select(b => new { b.Id, Name = b.Bank!.Name + " - " + b.Name })
                .OrderBy(b => b.Name).ToListAsync();
            if (branches.Count == 0)
            {
                Info("ابتدا در صفحه «شعب بانک‌ها» یک شعبه تعریف کنید.");
                return;
            }
            var branchChoices = branches.Select(b => new KeyValuePair<int, string>(b.Id, b.Name)).ToList();

            var curChoices = await db.Currencies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToListAsync();
            if (curChoices.Count == 0) { Info("ابتدا «واحدهای پول» را تعریف کنید."); return; }

            var companies = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.FullName)).ToListAsync();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToListAsync();
            var personChoices = people.Select(p => new KeyValuePair<int, string>(p.Id, p.Name)).ToList();
            personChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));
            var companyChoices = companies.ToList();
            companyChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var typeChoices = new List<KeyValuePair<int, string>>
            {
                new(0, "جاری"), new(1, "پس‌انداز"), new(2, "سپرده کوتاه‌مدت"), new(3, "سپرده بلندمدت")
            };

            var e = id is null ? null : await db.BankAccounts.FirstAsync(x => x.Id == id);

            // SignPeople is a native SQL json column: parse the stored array (or legacy plain text)
            // into one name per line for the editor.
            var signLines = "";
            var rawSign = e?.SignPeople;
            if (!string.IsNullOrWhiteSpace(rawSign))
            {
                try { signLines = string.Join(Environment.NewLine, JsonSerializer.Deserialize<List<string>>(rawSign) ?? []); }
                catch (JsonException) { signLines = rawSign; }
            }

            var dlg = new FieldEditorWindow(id is null ? "حساب بانکی جدید" : "ویرایش حساب بانکی", new[]
            {
                FieldSpec.Text_("نام/عنوان حساب", e?.Name, true),
                FieldSpec.Text_("شماره حساب", e?.AccountNumber, true),
                FieldSpec.Choice_("شعبه بانک", branchChoices, e?.BankBranchId ?? branchChoices[0].Key),
                FieldSpec.Choice_("نوع حساب", typeChoices, e?.AccountType ?? 0),
                FieldSpec.Choice_("واحد پول", curChoices, e?.CurrencyId ?? curChoices[0].Key),
                FieldSpec.Text_("شماره کارت", e?.CardNumber),
                FieldSpec.Text_("شماره شبا", e?.IdNumber),
                FieldSpec.Multi_("صاحبان امضا (هر نام در یک خط)", signLines),
                FieldSpec.Choice_("شخص مالک", personChoices, e?.PersonId ?? 0),
                FieldSpec.Choice_("شرکت مالک", companyChoices, e?.CompanyId ?? 0),
                FieldSpec.Numeric_("موجودی اولیه", e?.StartBalance),
                FieldSpec.Multi_("توضیحات", e?.Description)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new BankAccount { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now };
                db.BankAccounts.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            e.AccountNumber = dlg.GetText(1)!.Trim();
            e.BankBranchId = dlg.GetChoice(2) ?? branchChoices[0].Key;
            e.AccountType = dlg.GetChoice(3) ?? 0;
            e.CurrencyId = dlg.GetChoice(4) ?? curChoices[0].Key;
            e.CardNumber = dlg.GetText(5);
            e.IdNumber = dlg.GetText(6) ?? "";
            e.SignPeople = JsonSerializer.Serialize(
                (dlg.GetText(7) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0).ToList());
            e.PersonId = dlg.GetChoice(8) is > 0 ? dlg.GetChoice(8) : null;
            e.CompanyId = dlg.GetChoice(9) is > 0 ? dlg.GetChoice(9) : null;
            e.StartBalance = dlg.GetNumber(10) ?? 0;
            if (id is null) e.CurrentBalance = e.StartBalance; // editing never resets the live balance
            e.Description = dlg.GetText(11);
            e.UpdateDateTime = now;
            await db.SaveChangesAsync();
            Load();
            RefreshStatement();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک حساب را انتخاب کنید."); return; }
        if (MessageBox.Show($"حساب «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.BankAccounts.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            Load();
            RefreshStatement();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Reloads the statement for the still-selected row, or hides the card when none.</summary>
    private void RefreshStatement()
    {
        if (ListCtl.SelectedRow is RowBase r)
        {
            _stmtAccountId = null; // bypass the no-change guard
            ShowStatement(r.Id);
        }
        else
        {
            _stmtAccountId = null;
            StatementCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Mini statement for the selected account: current balance straight from ledger history
    /// (ChequeLedger.BalanceOfAsync) plus the last 12 transactions, newest first. Each row's
    /// running balance is derived by walking backwards from the current balance, so the
    /// numbers always agree with the ledger even when more than 12 rows exist.
    /// </summary>
    private async void ShowStatement(int accountId)
    {
        if (accountId == _stmtAccountId) return;
        _stmtAccountId = accountId;
        try
        {
            await using var db = SadrDb.New();
            var account = await db.BankAccounts.Where(a => a.Id == accountId)
                .Select(a => new { a.Name }).SingleOrDefaultAsync();
            if (account is null) { StatementCard.Visibility = Visibility.Collapsed; return; }

            var balance = await ChequeLedger.BalanceOfAsync(db, accountId);
            var txs = await db.BankTransactions
                .Where(t => t.BankAccountId == accountId && !t.Deleted)
                .OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Take(12)
                .Select(t => new { t.Kind, t.Amount, t.DateG, t.Describtion, t.EventKind })
                .ToListAsync();

            StatementPanel.Children.Clear();

            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Style = (Style)FindResource("H2"),
                Text = $"صورت‌حساب «{account.Name}» — {txs.Count} تراکنش آخر"
            };
            Grid.SetColumn(title, 0);
            head.Children.Add(title);
            var bal = new TextBlock
            {
                Style = (Style)FindResource("H2"),
                Text = "موجودی فعلی: " + balance.ToString("N0"),
                Foreground = B(balance < 0 ? "#D9534F" : "#1F8A3D")
            };
            Grid.SetColumn(bal, 1);
            head.Children.Add(bal);
            StatementPanel.Children.Add(head);

            var running = balance;
            foreach (var t in txs)
            {
                var deposit = t.Kind == Data.BankTransaction.KindDeposit;
                var after = running;                           // balance after this row (newest = current)
                running += deposit ? -t.Amount : t.Amount;     // step back to before this row

                var line = new Grid { Margin = new Thickness(2, 1, 2, 1) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

                var d = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = B("#44506B"),
                    Text = PersianDate.ToPersian(t.DateG)
                };
                Grid.SetColumn(d, 0);
                line.Children.Add(d);

                var desc = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = (t.EventKind == null ? "دستی — " : "چک — ") + t.Describtion,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = t.Describtion
                };
                Grid.SetColumn(desc, 1);
                line.Children.Add(desc);

                var amt = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 10, 0),
                    Text = (deposit ? "+ " : "− ") + t.Amount.ToString("N0"),
                    Foreground = B(deposit ? "#1F8A3D" : "#D9534F"),
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetColumn(amt, 2);
                line.Children.Add(amt);

                var run = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = "مانده: " + after.ToString("N0"),
                    Foreground = B("#44506B")
                };
                Grid.SetColumn(run, 3);
                line.Children.Add(run);

                StatementPanel.Children.Add(line);
            }

            StatementCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static System.Windows.Media.Brush B(string hex) => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex);

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
