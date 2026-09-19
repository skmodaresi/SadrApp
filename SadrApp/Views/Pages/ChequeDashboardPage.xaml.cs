using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Pages;

/// <summary>
/// Due-date dashboard: for the selected week (Saturday-based Persian week) it shows each
/// day's outstanding received/issued cheques with per-day and weekly totals, plus an
/// overdue section for cheques whose due date has already passed. Cashed/spent/cancelled
/// cheques are excluded because their money has already moved.
/// </summary>
public partial class ChequeDashboardPage : UserControl
{
    public class DayRow
    {
        public DateTime DateG { get; set; }
        public string Label { get; set; } = "";
        public int InCount { get; set; }
        public string InTotal { get; set; } = "—";
        public int OutCount { get; set; }
        public string OutTotal { get; set; } = "—";
        public string Tag { get; set; } = "";
    }

    public class DetailRow
    {
        public string Number { get; set; } = "";
        public string Amount { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Status { get; set; } = "";
        public string Account { get; set; } = "";
        public string Person { get; set; } = "";
        public string Note { get; set; } = "";
    }

    /// <summary>Outstanding-cheque projection shared by the week grid and the outlook.</summary>
    public class ChequeRow
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string Number { get; set; } = "";
        public int Direction { get; set; }
        public int Status { get; set; }
        public DateTime DateG { get; set; }
    }

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);

    private static readonly string[] WeekdayNames =
        { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنج‌شنبه", "جمعه" };

    public ObservableCollection<DayRow> Days { get; } = new();

    /// <summary>Saturday (start) of the currently displayed week.</summary>
    private DateTime _weekStart;

    /// <summary>The day whose cheques are shown in the details grid.</summary>
    private DateTime? _selectedDate;

    public ChequeDashboardPage()
    {
        InitializeComponent();
        DataContext = this;
        _weekStart = StartOfWeek(DateTime.Today);
        BtnPrevWeek.Click += (_, _) => { _weekStart = _weekStart.AddDays(-7); Load(); };
        BtnNextWeek.Click += (_, _) => { _weekStart = _weekStart.AddDays(7); Load(); };
        BtnThisWeek.Click += (_, _) => { _weekStart = StartOfWeek(DateTime.Today); Load(); };        DayGrid.SelectionChanged += (_, _) =>
        {
            if (DayGrid.SelectedItem is DayRow d)
            {
                _selectedDate = d.DateG;
                ShowDayDetails(d.DateG);
            }
        };
        Loaded += (_, _) => Load();
    }

    private static DateTime StartOfWeek(DateTime d) =>
        d.Date.AddDays(-(((int)d.DayOfWeek + 1) % 7)); // Saturday = 0 in Persian weeks

    private async void Load()
    {
        try
        {
            var weekEnd = _weekStart.AddDays(7);
            var today = DateTime.Today;
            _selectedDate = null;
            DetailGrid.ItemsSource = null;
            TxtDetailsTitle.Text = "چک‌های روز انتخاب‌شده";

            await using var db = SadrDb.New();

            // Outstanding cheques: anything not yet cashed/spent/cancelled/deleted.
            var outstanding = await db.Cheques.Where(c =>
                    !c.Deleted &&
                    c.Status != ChequesPage.StatusCashed &&
                    c.Status != ChequesPage.StatusSpent &&
                    c.Status != ChequesPage.StatusCanceled)
                .Select(c => new ChequeRow
                {
                    Id = c.Id, Amount = c.Amount, Number = c.Number,
                    Direction = c.Direction, Status = c.Status, DateG = c.DateG
                })
                .ToListAsync();

            Days.Clear();
            foreach (var i in Enumerable.Range(0, 7))
            {
                var day = _weekStart.AddDays(i);
                var dayCheques = outstanding.Where(c => c.DateG.Date == day).ToList();
                var ins = dayCheques.Where(c => c.Direction == ChequesPage.DirectionReceived).ToList();
                var outs = dayCheques.Where(c => c.Direction == ChequesPage.DirectionIssued).ToList();
                Days.Add(new DayRow
                {
                    DateG = day,
                    Label = WeekdayNames[(int)day.DayOfWeek] + " " + PersianDate.ToPersian(day),
                    InCount = ins.Count,
                    InTotal = ins.Count == 0 ? "—" : ins.Sum(c => c.Amount).ToString("N0"),
                    OutCount = outs.Count,
                    OutTotal = outs.Count == 0 ? "—" : outs.Sum(c => c.Amount).ToString("N0"),
                    Tag = day == today ? "امروز" : day < today ? "گذشته" : ""
                });
            }

            var weekCheques = outstanding.Where(c => c.DateG.Date >= _weekStart && c.DateG.Date < weekEnd).ToList();
            TxtWeekIn.Text = weekCheques.Where(c => c.Direction == ChequesPage.DirectionReceived)
                .Sum(c => c.Amount).ToString("N0");
            TxtWeekOut.Text = weekCheques.Where(c => c.Direction == ChequesPage.DirectionIssued)
                .Sum(c => c.Amount).ToString("N0");

            TxtWeekLabel.Text = $"هفته {PersianDate.ToPersian(_weekStart)} تا {PersianDate.ToPersian(weekEnd.AddDays(-1))}";

            // Overdue section: outstanding cheques with a due date before today, grouped by day.
            var overdue = outstanding.Where(c => c.DateG.Date < today).ToList();
            if (overdue.Count == 0)
            {
                OverdueCard.Visibility = Visibility.Collapsed;
            }
            else
            {
                OverduePanel.Children.Clear();
                foreach (var g in overdue.GroupBy(c => c.DateG.Date).OrderBy(g => g.Key))
                {
                    var ins = g.Where(c => c.Direction == ChequesPage.DirectionReceived).ToList();
                    var outs = g.Where(c => c.Direction == ChequesPage.DirectionIssued).ToList();
                    var daysLate = (today - g.Key).Days;
                    var line = new TextBlock
                    {
                        Margin = new Thickness(2, 2, 2, 2),
                        Text = $"⚠️ سررسید گذشته {PersianDate.ToPersian(g.Key)} ({daysLate} روز پیش): " +
                               $"وصولی {ins.Sum(c => c.Amount):N0} ({ins.Count} چک) — " +
                               $"پرداختی {outs.Sum(c => c.Amount):N0} ({outs.Count} چک)"
                    };
                    var date = g.Key;
                    line.Cursor = Cursors.Hand;
                    line.MouseLeftButtonUp += (_, _) =>
                    {
                        _selectedDate = date;
                        ShowDayDetails(date);
                    };
                    OverduePanel.Children.Add(line);
                }
                OverdueCard.Visibility = Visibility.Visible;
            }

            // Default the details pane to today (or the first day that has cheques).
            var firstWithCheques = Days.FirstOrDefault(d => d.InCount + d.OutCount > 0);
            if (firstWithCheques is not null)
            {
                DayGrid.SelectedItem = firstWithCheques;
                _selectedDate = firstWithCheques.DateG;
                ShowDayDetails(firstWithCheques.DateG);
            }

            await BuildOutlook(db, outstanding);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری داشبورد", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Net-position outlook: outstanding cheques over the next 30 days, aggregated into
    /// weekly buckets, with a running net position starting from today's combined ledger
    /// balance of all accounts. The cumulative bar makes it obvious when projected
    /// spending would outpace incoming money.
    /// </summary>
    private async Task BuildOutlook(SadrDbContext db, List<ChequeRow> outstanding)
    {
        var today = DateTime.Today;
        var horizonEnd = today.AddDays(30);

        var startBalance = await db.BankAccounts.Where(a => !a.Deleted).SumAsync(a => (decimal?)a.CurrentBalance) ?? 0m;
        var future = outstanding.Where(c => c.DateG.Date >= today && c.DateG.Date <= horizonEnd).ToList();

        OutlookPanel.Children.Clear();

        var header = new TextBlock { Style = (Style)FindResource("H2"), Margin = new Thickness(2, 0, 2, 4) };
        if (future.Count == 0)
        {
            header.Text = "چشم‌انداز ۳۰ روز آینده: چکی در این بازه سررسید نمی‌شود.";
            OutlookPanel.Children.Add(header);
            return;
        }

        var running = startBalance;
        var worst = running;
        var maxAbs = 1m;
        var buckets = new List<(DateTime From, DateTime To, decimal In, decimal Out, decimal Net, decimal Cumulative)>();
        foreach (var weekStart in Enumerable.Range(0, 5).Select(i => today.AddDays(7 * i)))
        {
            var to = weekStart.AddDays(6).Date > horizonEnd ? horizonEnd : weekStart.AddDays(6).Date;
            if (weekStart.Date > horizonEnd) break;
            var inWeek = future.Where(c => c.DateG.Date >= weekStart && c.DateG.Date <= to).ToList();
            var @in = inWeek.Where(c => c.Direction == ChequeConsts.DirectionReceived).Sum(c => c.Amount);
            var @out = inWeek.Where(c => c.Direction == ChequeConsts.DirectionIssued).Sum(c => c.Amount);
            running += @in - @out;
            if (running < worst) worst = running;
            maxAbs = new[] { maxAbs, Math.Abs(@in), Math.Abs(@out), Math.Abs(running), Math.Abs(startBalance) }.Max();
            buckets.Add((weekStart, to, @in, @out, @in - @out, running));
        }

        header.Text = $"چشم‌انداز ۳۰ روز آینده — موجودی فعلی کل حساب‌ها: {startBalance:N0}" +
                      $" | پایان دوره: {running:N0}";
        header.                Foreground = worst < 0 ? B("#D9534F") : B("#1F8A3D");
        OutlookPanel.Children.Add(header);

        if (worst < 0)
        {
            OutlookPanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(2, 0, 2, 4),
                Foreground = B("#B25B00"),
                Text = $"⚠️ کسری پیش‌بینی می‌شود: پایین‌ترین موجودی {worst:N0} — ورود منابع یا تعویق پرداخت لازم است."
            });
        }

        var barWidth = Math.Max(560, OutlookPanel.ActualWidth > 0 ? OutlookPanel.ActualWidth - 40 : 700);
        foreach (var b in buckets)
        {
            var row = new Grid { Margin = new Thickness(2, 1, 2, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{PersianDate.ToPersian(b.From)} تا {PersianDate.ToPersian(b.To)}"
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var canvas = new Canvas { Height = 20, VerticalAlignment = VerticalAlignment.Center };
            var zeroX = barWidth / 2;
            void AddBar(decimal value, bool positive)
            {
                if (value == 0) return;
                var w = (double)(Math.Abs(value) / maxAbs) * (barWidth / 2 - 10);
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Height = 16,
                    Width = Math.Max(2, w),
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = positive ? B("#4C9F70") : B("#D9534F"),
                    ToolTip = (positive ? "وصولی: " : "پرداختی: ") + value.ToString("N0")
                };
                Canvas.SetTop(rect, 2);
                Canvas.SetLeft(rect, positive ? zeroX : zeroX - Math.Max(2, w));
                canvas.Children.Add(rect);
            }

            AddBar(b.In, true);
            AddBar(b.Out, false);
            var cum = new System.Windows.Shapes.Rectangle
            {
                Height = 4,
                Width = Math.Max(2, (double)(Math.Abs(b.Cumulative) / maxAbs) * (barWidth / 2 - 10)),
                RadiusX = 2,
                RadiusY = 2,
                Fill = B("#44506B"),
                Opacity = 0.55,
                ToolTip = $"موجودی تجمعی در پایان بازه: {b.Cumulative:N0}"
            };
            Canvas.SetTop(cum, 17);
            Canvas.SetLeft(cum, b.Cumulative >= 0 ? zeroX : zeroX - cum.Width);
            canvas.Children.Add(cum);
            Grid.SetColumn(canvas, 1);
            row.Children.Add(canvas);

            var tag = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                MinWidth = 110,
                Text = $"خالص {b.Net:N0} | باقی {b.Cumulative:N0}",
                Foreground = B(b.Net < 0 ? "#D9534F" : "#1F8A3D")
            };
            Grid.SetColumn(tag, 2);
            row.Children.Add(tag);

            OutlookPanel.Children.Add(row);
        }
    }

    private async void ShowDayDetails(DateTime date)
    {
        try
        {
            await using var db = SadrDb.New();
            var rows = await db.Cheques.Where(c => !c.Deleted && c.DateG.Date == date.Date)
                .OrderBy(c => c.Direction).ThenBy(c => c.DateG)
                .Select(c => new
                {
                    c.Amount, c.Number, c.Direction, c.Status, c.Describtion, c.BankAccountId,
                    Person = c.PersonId != null ? c.Person.FirstName + " " + c.Person.LastName : "",
                    c.RecieverFullName
                })
                .ToListAsync();

            var accountNames = await db.BankAccounts.Where(a => !a.Deleted)
                .Select(a => new { a.Id, a.Name }).ToDictionaryAsync(a => a.Id, a => a.Name);

            DetailGrid.ItemsSource = rows.Select(r => new DetailRow
            {
                Number = r.Number,
                Amount = r.Amount.ToString("N0"),
                Direction = ChequesPage.DirectionNames.GetValueOrDefault(r.Direction, ""),
                Status = ChequesPage.StatusNames.GetValueOrDefault(r.Status, ""),
                Account = accountNames.TryGetValue(r.BankAccountId, out var an) ? an : "حساب #" + r.BankAccountId,
                Person = new[] { r.RecieverFullName, r.Person }.Where(s => !string.IsNullOrEmpty(s))
                    .Aggregate("", (x, y) => x.Length == 0 ? y : x + " | " + y),
                Note = r.Describtion
            }).ToList();
            TxtDetailsTitle.Text = "چک‌های " + PersianDate.ToPersian(date);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
