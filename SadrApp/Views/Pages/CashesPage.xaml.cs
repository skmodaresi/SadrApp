using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;
using SadrApp.ViewModels;
using SadrApp.Views.Controls;

namespace SadrApp.Views.Pages;

/// <summary>Management page for the Cashes table: cash boxes with start balance, owners, and signatories.</summary>
public partial class CashesPage : UserControl
{
    public CashesPage()
    {
        InitializeComponent();
        ListCtl.NewClicked += (_, _) => ShowEditor(null);
        ListCtl.EditClicked += (_, _) => Edit();
        ListCtl.DeleteClicked += (_, _) => Delete();
        ListCtl.RefreshClicked += (_, _) => Load();
        ListCtl.RowDoubleClicked += (_, _) => Edit();
        ListCtl.InnerGrid.SelectionChanged += (_, _) =>
        {
            if (ListCtl.SelectedRow is RowBase r) ShowStatement(r.Id);
        };
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        try
        {
            await using var db = SadrDb.New();
            var people = await db.People.Where(p => !p.Deleted)
                .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName }).ToDictionaryAsync(p => p.Id, p => p.Name);
            var companies = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.FullName }).ToDictionaryAsync(c => c.Id, c => c.FullName);

            var rows = await db.Cashes.Where(c => !c.Deleted)
                .Select(c => new
                {
                    c.Id, c.Name, c.Code, c.StartBalance, c.CurrentBalance, c.Status,
                    c.OwnerPersonId, c.OwnerCompanyId, c.ResponcePersonId, c.Description
                })
                .ToListAsync();

            ListCtl.SetRows(rows.Select(c => new RowBase
            {
                Id = c.Id,
                Title = c.Name,
                Code = c.Code ?? "",
                Extra = "موجودی: " + c.CurrentBalance.ToString("N0"),
                Description = string.Join(" | ",
                    c.Status == 1 ? "فعال" : "غیرفعال",
                    c.OwnerPersonId is int op && people.TryGetValue(op, out var on) ? "مالک: " + on : "",
                    c.OwnerCompanyId is int oc && companies.TryGetValue(oc, out var cn) ? "شرکت: " + cn : "",
                    people.TryGetValue(c.ResponcePersonId, out var rn) ? "مسئول: " + rn : "",
                    c.Description ?? "").Trim(" |".ToCharArray())
            }).ToList());
            ListCtl.ShowExtraColumn("موجودی");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Transaction history for the selected cash box, mirroring the bank-account statement:
    /// newest first with a running balance rebuilt backwards from the current balance.
    /// </summary>
    private async void ShowStatement(int cashId)
    {
        try
        {
            await using var db = SadrDb.New();
            var cash = await db.Cashes.Where(c => c.Id == cashId)
                .Select(c => new { c.Name, c.Code, c.CurrentBalance }).SingleOrDefaultAsync();
            if (cash is null) { StatementCard.Visibility = Visibility.Collapsed; return; }

            var txs = await db.AccountTransactions
                .Where(t => !t.Deleted && t.CashId == cashId)
                .OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Take(12)
                .Select(t => new { t.Type, t.Value, t.DateG, t.Description })
                .ToListAsync();

            StatementPanel.Children.Clear();

            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Style = (Style)FindResource("H2"),
                Text = $"صورت‌حساب «{cash.Name}»"
                       + (string.IsNullOrWhiteSpace(cash.Code) ? "" : $" ({cash.Code})")
                       + $" — {txs.Count} تراکنش آخر"
            };
            Grid.SetColumn(title, 0);
            head.Children.Add(title);
            var bal = new TextBlock
            {
                Style = (Style)FindResource("H2"),
                Text = "موجودی فعلی: " + cash.CurrentBalance.ToString("N0"),
                Foreground = B(cash.CurrentBalance < 0 ? "#D9534F" : "#1F8A3D")
            };
            Grid.SetColumn(bal, 1);
            head.Children.Add(bal);
            StatementPanel.Children.Add(head);

            var running = cash.CurrentBalance;
            foreach (var t in txs)
            {
                var deposit = t.Type == AccountTransactionTypeConsts.Receipt
                           || t.Type == AccountTransactionTypeConsts.Transfer;
                var after = running;                     // balance after this row (newest = current)
                running += deposit ? -t.Value : t.Value; // step back to before this row

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
                    Text = t.Description,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = t.Description
                };
                Grid.SetColumn(desc, 1);
                line.Children.Add(desc);

                var amt = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 10, 0),
                    Text = (deposit ? "+ " : "− ") + t.Value.ToString("N0"),
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

    private static System.Windows.Media.Brush B(string hex) =>
        (System.Windows.Media.Brush)(new System.Windows.Media.BrushConverter().ConvertFromString(hex)
            ?? System.Windows.Media.Brushes.Black);

    private void Edit()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک صندوق را انتخاب کنید."); return; }
        ShowEditor(row.Id);
    }

    private async void ShowEditor(int? id)
    {
        try
        {
            await using var db = SadrDb.New();
            var personChoices = await db.People.Where(p => !p.Deleted)
                .Select(p => new KeyValuePair<int, string>(p.Id, p.FirstName + " " + p.LastName)).ToListAsync();
            if (personChoices.Count == 0) { Info("ابتدا حداقل یک شخص تعریف کنید (برای مسئول صندوق)."); return; }
            var companyChoices = await db.Companies.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.FullName)).ToListAsync();
            companyChoices.Insert(0, new KeyValuePair<int, string>(0, "— انتخاب نشده —"));

            var statusChoices = new List<KeyValuePair<int, string>>
            {
                new(1, "فعال"), new(0, "غیرفعال")
            };

            var e = id is null ? null : await db.Cashes.FirstAsync(x => x.Id == id);

            // SignPeople is a native SQL json column: parse the stored array (or legacy plain text)
            // into one name per line for the editor, exactly like bank accounts.
            var signLines = "";
            var rawSign = e?.SignPeople;
            if (!string.IsNullOrWhiteSpace(rawSign))
            {
                try { signLines = string.Join(Environment.NewLine, JsonSerializer.Deserialize<List<string>>(rawSign) ?? []); }
                catch (JsonException) { signLines = rawSign; }
            }

            var dlg = new FieldEditorWindow(id is null ? "صندوق جدید" : "ویرایش صندوق", new[]
            {
                FieldSpec.Text_("نام صندوق", e?.Name, true),
                FieldSpec.Text_("کد صندوق", e?.Code),
                FieldSpec.Choice_("مسئول صندوق", personChoices, e?.ResponcePersonId ?? personChoices[0].Key),
                FieldSpec.Choice_("مالک (شخص)", personChoices, e?.OwnerPersonId ?? 0),
                FieldSpec.Choice_("مالک (شرکت)", companyChoices, e?.OwnerCompanyId ?? 0),
                FieldSpec.Choice_("وضعیت", statusChoices, e?.Status ?? 1),
                FieldSpec.Numeric_("موجودی اولیه", e?.StartBalance),
                FieldSpec.Multi_("صاحبان امضا (هر نام در یک خط)", signLines),
                FieldSpec.Multi_("توضیحات", e?.Description)
            }) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            if (e is null)
            {
                e = new Cash { RecordUniqueId = Guid.NewGuid(), CreateDateTime = now, CreateUserId = UserSession.CurrentUserId };
                db.Cashes.Add(e);
            }
            e.Name = dlg.GetText(0)!.Trim();
            var code = (dlg.GetText(1) ?? "").Trim();
            if (code.Length > 0 && await db.Cashes.AnyAsync(x => !x.Deleted && x.Code == code && x.Id != id))
            {
                MessageBox.Show(CodeRules.MsgCodeDuplicate, "ذخیره ممکن نیست", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            e.Code = code.Length > 0 ? code : null;
            e.ResponcePersonId = dlg.GetChoice(2) ?? personChoices[0].Key;
            e.OwnerPersonId = dlg.GetChoice(3) is > 0 ? dlg.GetChoice(3) : null;
            e.OwnerCompanyId = dlg.GetChoice(4) is > 0 ? dlg.GetChoice(4) : null;
            e.Status = dlg.GetChoice(5) ?? 1;
            e.StartBalance = dlg.GetNumber(6) ?? 0;
            if (id is null) e.CurrentBalance = e.StartBalance; // edits never reset the live balance
            e.SignPeople = JsonSerializer.Serialize(
                (dlg.GetText(7) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0).ToList());
            e.Description = dlg.GetText(8);
            e.UpdateDateTime = now;
            e.UpdateUserId = UserSession.CurrentUserId;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Delete()
    {
        if (ListCtl.SelectedRow is not RowBase row) { Info("ابتدا یک صندوق را انتخاب کنید."); return; }
        if (MessageBox.Show($"صندوق «{row.Title}» حذف شود؟", "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await using var db = SadrDb.New();
            var e = await db.Cashes.FirstAsync(x => x.Id == row.Id);
            e.Deleted = true;
            e.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Info(string m) => MessageBox.Show(m, "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
}
