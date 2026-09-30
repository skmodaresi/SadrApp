using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// Keeps a DetailAccount (حساب تفصیلی) in sync with the SubSidiaryAccount (حساب معین)
/// the user picks in the People/Companies editor:
/// - same Code as the person/company (کد ملی for people)
/// - linked through the CustomersProviders mirror (DetailAccount.CustomerProviderId →
///   CustomersProviders.PersonId / CompanyId)
/// - picking «— بدون حساب معین —» leaves any existing row untouched.
/// </summary>
public static class DetailAccountService
{
    /// <summary>SubSidiaryAccount currently linked to the person (null = none). For editor preselect.</summary>
    public static async Task<int?> GetCurrentSubSidiaryForPersonAsync(SadrDbContext db, int personId)
    {
        var cp = await db.CustomersProviders.FirstOrDefaultAsync(c => c.PersonId == personId);
        if (cp is null) return null;
        var d = await db.DetailAccounts.FirstOrDefaultAsync(x => x.CustomerProviderId == cp.Id && !x.Deleted);
        return d?.SubSidiaryAccountId;
    }

    /// <summary>SubSidiaryAccount currently linked to the company (null = none). For editor preselect.</summary>
    public static async Task<int?> GetCurrentSubSidiaryForCompanyAsync(SadrDbContext db, int companyId)
    {
        var cp = await db.CustomersProviders.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        if (cp is null) return null;
        var d = await db.DetailAccounts.FirstOrDefaultAsync(x => x.CustomerProviderId == cp.Id && !x.Deleted);
        return d?.SubSidiaryAccountId;
    }

    /// <summary>Call after the person (and its CustomersProviders mirror) is saved.</summary>
    public static Task SyncPersonAsync(SadrDbContext db, Person person, int? subsidiaryAccountId) =>
        SyncAsync(db, cp => cp.PersonId == person.Id,
            DisplayName(person.Title, person.FirstName, person.LastName), person.Code, subsidiaryAccountId);

    /// <summary>Call after the company (and its CustomersProviders mirror) is saved.</summary>
    public static Task SyncCompanyAsync(SadrDbContext db, Company company, int? subsidiaryAccountId) =>
        SyncAsync(db, cp => cp.CompanyId == company.Id,
            company.FullName.Trim(), company.Code, subsidiaryAccountId);

    /// <summary>
    /// Startup backfill (mirrors CustomersProviderService.BackfillAsync): creates a DetailAccount
    /// for every person/company that has a CustomersProviders row but no live detail account yet.
    /// Conservative rules: the person/company must have a Code, a target حساب معین is inferred from
    /// an existing live detail account with the same code (otherwise the row is skipped), and codes
    /// that would collide with an existing live detail account are never created.
    /// </summary>
    public static async Task BackfillAsync(SadrDbContext db)
    {
        var subIds = (await db.SubSidiaryAccounts.Where(s => !s.Deleted).Select(s => s.Id).ToListAsync()).ToHashSet();
        if (subIds.Count == 0) return; // no حساب معین defined yet — nothing to attach to

        var details = await db.DetailAccounts.ToListAsync(); // incl. soft-deleted, for revive checks
        var detailByCp = details.Where(d => d.CustomerProviderId is int cpId)
            .GroupBy(d => d.CustomerProviderId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var liveCodeOwner = details.Where(d => !d.Deleted)
            .GroupBy(d => d.Code).ToDictionary(g => g.Key, g => g.First().Id);

        var people = await db.People.Where(p => !p.Deleted).Select(p => new { p.Id, p.Title, p.FirstName, p.LastName, p.Code })
            .ToDictionaryAsync(p => p.Id);
        var companies = await db.Companies.Where(c => !c.Deleted).Select(c => new { c.Id, c.FullName, c.Code })
            .ToDictionaryAsync(c => c.Id);

        var mirrors = await db.CustomersProviders
            .Where(c => !c.Deleted && (c.PersonId != null || c.CompanyId != null)).ToListAsync();

        var now = DateTime.Now;
        var created = 0;
        foreach (var cp in mirrors)
        {
            if (detailByCp.TryGetValue(cp.Id, out var own) && own.Any(d => !d.Deleted)) continue; // already has one

            string name, code, source;
            int? targetSub = null;
            if (cp.PersonId is int pid && people.TryGetValue(pid, out var p))
            {
                name = DisplayName(p.Title, p.FirstName, p.LastName);
                code = p.Code?.Trim() ?? "";
                source = "صفحه اشخاص";
            }
            else if (cp.CompanyId is int cid && companies.TryGetValue(cid, out var c))
            {
                name = c.FullName.Trim();
                code = c.Code?.Trim() ?? "";
                source = "صفحه شرکت‌ها";
            }
            else continue; // mirror without a live person/company behind it

            if (code.Length == 0) continue; // no code → cannot mirror a code-bearing account

            // Target حساب معین: reuse an existing live detail account with the same code.
            targetSub = details.FirstOrDefault(d => !d.Deleted && d.Code == code)?.SubSidiaryAccountId;
            if (targetSub is not int sub || !subIds.Contains(sub)) continue; // nothing to infer from → skip

            // Never collide with an existing live detail account code.
            if (liveCodeOwner.TryGetValue(code, out var ownerId) &&
                !own.Any(d => d.Id == ownerId)) continue;

            var detail = new DetailAccount
            {
                CustomerProviderId = cp.Id,
                SubSidiaryAccountId = sub,
                Code = code,
                Name = name,
                AccountType = AccountTypeConsts.Both,
                Description = "ایجاد خودکار از " + source,
                RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = now,
                UpdateDateTime = now
            };
            db.DetailAccounts.Add(detail);
            created++;
        }
        if (created > 0) await db.SaveChangesAsync();
    }

    private static async Task SyncAsync(SadrDbContext db, System.Linq.Expressions.Expression<Func<CustomersProvider, bool>> mirrorKey,
        string name, string? code, int? subsidiaryAccountId)
    {
        // «بدون حساب معین» یا بدون کد → دست نمی‌زنیم (ردیف‌های حسابداری بی‌اجازه حذف/تغییر نمی‌کنند)
        if (subsidiaryAccountId is not int subId || subId <= 0) return;
        if (string.IsNullOrWhiteSpace(code)) return;

        var cp = await db.CustomersProviders.FirstOrDefaultAsync(mirrorKey);
        if (cp is null) return; // mirror row is created by CustomersProviderService on save

        var detail = await db.DetailAccounts.FirstOrDefaultAsync(d => d.CustomerProviderId == cp.Id);
        if (detail is null)
        {
            detail = new DetailAccount
            {
                RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now,
                AccountType = AccountTypeConsts.Both,
                Description = "ایجاد خودکار از " + (cp.PersonId != null ? "صفحه اشخاص" : "صفحه شرکت‌ها")
            };
            db.DetailAccounts.Add(detail);
        }
        detail.CustomerProviderId = cp.Id;
        detail.SubSidiaryAccountId = subId;
        detail.Code = code.Trim();
        detail.Name = name;
        detail.Deleted = false; // re-linking revives a previously soft-deleted auto row
        detail.UpdateDateTime = DateTime.Now;
        await db.SaveChangesAsync();
    }

    private static string DisplayName(string? title, string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(title)
            ? (firstName + " " + lastName).Trim()
            : $"{title} {firstName} {lastName}".Trim();
}
