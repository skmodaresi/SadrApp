// End-to-end money-flow test against a dedicated throwaway database:
//   1. bootstrap fresh DB (EnsureDatabase + SeedData)
//   2. create a cash box
//   3. post a receipt (cash), a payment (cash), and two invoice payments
//      linked through InvoiceMoneyTransactions
//   4. pay a task report's cost from the cash box via TaskReportMoneyTransactions
//   5. verify balances and links, print PASS/FAIL per check, exit 1 on failure
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

const string DbName = "SadrE2ECheck";
var cs = $@"Data Source=.\SQLExpress;Initial Catalog={DbName};Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Encrypt=True";
DbConfig.Save(cs);
Database.Reset();

var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
using (var con = new SqlConnection(master))
{
    con.Open();
    using var cmd = con.CreateCommand();
    cmd.CommandText = $"IF DB_ID(N'{DbName}') IS NOT NULL BEGIN ALTER DATABASE {DbName} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {DbName}; END";
    cmd.CommandTimeout = 0;
    cmd.ExecuteNonQuery();
}

DbBootstrapper.EnsureDatabase();
SeedData.Run();

int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!ok) failures++;
}

// ---------- 1) cash box ----------
await using (var db = SadrDb.New())
{
    // SeedData does not create people; the harness creates everything it needs.
    var person = db.People.Add(new Person
    {
        FirstName = "کاربر", LastName = "تست",
        RecordUniqueId = Guid.NewGuid()
    }).Entity;
    await db.SaveChangesAsync();
    var currency = await db.Currencies.FirstAsync();
    var cash = new Cash
    {
        Name = "صندوق تست",
        StartBalance = 1_000_000m,
        CurrentBalance = 1_000_000m,
        Status = 1,
        ResponcePersonId = person.Id,
        SignPeople = "[\"امضاکننده نمونه\"]",
        RecordUniqueId = Guid.NewGuid()
    };
    db.Cashes.Add(cash);

    // project + task for the task-report cost flow
    var category = await db.ProjectCategories.FirstOrDefaultAsync()
        ?? db.ProjectCategories.Add(new ProjectCategory { Name = "دستهٔ تست", RecordUniqueId = Guid.NewGuid() }).Entity;
    await db.SaveChangesAsync();
    var project = db.Projects.Add(new Project
    {
        Name = "پروژهٔ تست",
        ProjectType = 1,
        StartDate = "1405/07/01", StartDateG = new DateTime(2026, 9, 23),
        EndDate = "1405/10/01", EndDateG = new DateTime(2026, 12, 22),
        CurrencyId = currency.Id,
        Status = 1,
        CategoryId = category.Id,
        CreatorPersonId = person.Id,
        ProjectManagerPersonId = person.Id,
        GUID = Guid.NewGuid(),
        RecordUniqueId = Guid.NewGuid()
    }).Entity;
    await db.SaveChangesAsync();
    var task = db.ProjectTasks.Add(new ProjectTask
    {
        ProjectId = project.Id,
        Name = "وظیفهٔ تست",
        StartDate = "1405/07/02", StartDateG = new DateTime(2026, 9, 24),
        DueDate = "1405/08/02", DueDateG = new DateTime(2026, 10, 24),
        WorkingDays = 30,
        ResponcePersonId = person.Id,
        StartBudget = 0,
        RecordUniqueId = Guid.NewGuid()
    }).Entity;
    await db.SaveChangesAsync();

    // invoice to receive the two payments
    var invoice = db.Invoices.Add(new Invoice
    {
        InvoiceDate = "1405/07/05", InvoiceDateG = new DateTime(2026, 9, 27),
        InvoiceNumber = "E2E-1",
        TotalPrice = 800_000m,
        InvoiceType = InvoiceTypeConsts.Sell,
        Status = 1,
        RecordUniqueId = Guid.NewGuid()
    }).Entity;
    await db.SaveChangesAsync();

    // ---------- 2) receipt +500,000 into the cash box ----------
    var receipt = new AccountTransaction
    {
        Type = AccountTransactionTypeConsts.Receipt,
        TransActionSystem = TransactionSystemConsts.Cash,
        CashId = cash.Id,
        Value = 500_000m,
        DateG = DateTime.Today,
        Date = PersianDate.ToPersian(DateTime.Today),
        Description = "دریافت تست",
        RecordUniqueId = Guid.NewGuid()
    };
    await CashLedger.PostAsync(db, receipt);

    // ---------- 3) payment -200,000 out of the cash box ----------
    var payment = new AccountTransaction
    {
        Type = AccountTransactionTypeConsts.Payment,
        TransActionSystem = TransactionSystemConsts.Cash,
        CashId = cash.Id,
        Value = 200_000m,
        DateG = DateTime.Today,
        Date = PersianDate.ToPersian(DateTime.Today),
        Description = "پرداخت تست",
        RecordUniqueId = Guid.NewGuid()
    };
    await CashLedger.PostAsync(db, payment);

    // ---------- 4) two invoice payments (several per invoice) ----------
    foreach (var amount in new[] { 300_000m, 100_000m })
    {
        var t = new AccountTransaction
        {
            Type = AccountTransactionTypeConsts.Receipt,
            TransActionSystem = TransactionSystemConsts.Cash,
            CashId = cash.Id,
            InvoiceId = invoice.Id,
            Value = amount,
            DateG = DateTime.Today,
            Date = PersianDate.ToPersian(DateTime.Today),
            Description = "پرداخت فاکتور E2E-1",
            RecordUniqueId = Guid.NewGuid()
        };
        await CashLedger.PostAsync(db, t);
        db.InvoiceMoneyTransactions.Add(new InvoiceMoneyTransaction
        {
            InvoiceId = invoice.Id,
            AccountTransactionId = t.Id,
            RecordUniqueId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    // ---------- 5) task report cost paid from the cash box ----------
    TaskService.EnsureSchema(db);
    var report = db.TaskReports.Add(new TaskReport
    {
        TaskId = task.Id,
        FreeTaskId = 0,
        ReportDate = PersianDate.ToPersian(DateTime.Today),
        Description = "گزارش تست با هزینه",
        ReporterUserId = person.Id,
        Cost = 300_000m,
        RecordUniqueId = Guid.NewGuid()
    }).Entity;
    await db.SaveChangesAsync();
    var costTx = new AccountTransaction
    {
        Type = AccountTransactionTypeConsts.Payment,
        TransActionSystem = TransactionSystemConsts.Cash,
        CashId = cash.Id,
        Value = report.Cost,
        DateG = DateTime.Today,
        Date = PersianDate.ToPersian(DateTime.Today),
        PersonId = person.Id,
        Description = "هزینه گزارش: گزارش تست با هزینه",
        RecordUniqueId = Guid.NewGuid()
    };
    await CashLedger.PostAsync(db, costTx);
    db.TaskReportMoneyTransactions.Add(new TaskReportMoneyTransaction
    {
        TaskReportId = report.Id,
        AccountTransactionId = costTx.Id,
        RecordUniqueId = Guid.NewGuid()
    });
    await db.SaveChangesAsync();

    // ---------- verification ----------
    var cash2 = await db.Cashes.AsNoTracking().FirstAsync(c => c.Id == cash.Id);
    // 1,000,000 start + 500,000 receipt + 300,000 + 100,000 invoice payments
    //            − 200,000 payment − 300,000 report cost = 1,400,000
    Check("cash balance after all flows", cash2.CurrentBalance == 1_400_000m,
        $"actual={cash2.CurrentBalance:N0} expected=1,400,000");

    var invLinks = await db.InvoiceMoneyTransactions.AsNoTracking()
        .Where(l => !l.Deleted && l.InvoiceId == invoice.Id).ToListAsync();
    Check("invoice has 2 live money-transaction links", invLinks.Count == 2,
        $"actual={invLinks.Count}");

    var invPaid = await db.InvoiceMoneyTransactions.AsNoTracking()
        .Where(l => !l.Deleted && l.InvoiceId == invoice.Id)
        .Join(db.AccountTransactions.Where(t => !t.Deleted),
            l => l.AccountTransactionId, t => t.Id, (l, t) => t.Value)
        .SumAsync(x => x);
    Check("invoice paid total = 400,000", invPaid == 400_000m, $"actual={invPaid:N0}");

    var repLink = await db.TaskReportMoneyTransactions.AsNoTracking()
        .FirstOrDefaultAsync(l => !l.Deleted && l.TaskReportId == report.Id);
    Check("task report has a money-transaction link", repLink is not null);
    if (repLink is not null)
    {
        var linked = await db.AccountTransactions.AsNoTracking()
            .FirstAsync(t => t.Id == repLink.AccountTransactionId);
        Check("report link points at the 300,000 cash payment",
            linked.CashId == cash.Id && linked.Value == 300_000m
            && linked.Type == AccountTransactionTypeConsts.Payment);
    }

    // optional sides stay null (no fake counterpart ids)
    var all = await db.AccountTransactions.AsNoTracking()
        .Where(t => !t.Deleted && t.CashId == cash.Id).ToListAsync();
    Check("cash-side rows have null BankAccountId", all.All(t => t.BankAccountId is null));

    var bankBalance = await db.BankAccounts.AsNoTracking()
        .Select(a => (decimal?)a.CurrentBalance).FirstOrDefaultAsync() ?? 0m;
    Check("bank balances untouched by cash flows", bankBalance == 0m, $"actual={bankBalance:N0}");
}

Console.WriteLine(failures == 0 ? "E2E PASSED" : $"E2E FAILED: {failures} check(s)");
return failures == 0 ? 0 : 1;
