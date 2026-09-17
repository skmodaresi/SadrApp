using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// Keeps CustomersProviders in sync with People and Companies:
/// - saving a person/company creates or updates its mirrored CustomersProviders row
/// - deleting a person/company soft-deletes the mirrored row (kept alive if invoices or
///   detail accounts reference it, since those FKs must stay valid)
/// </summary>
public static class CustomersProviderService
{
    /// <summary>Call after a person is saved or soft-deleted.</summary>
    public static async Task SyncPersonAsync(SadrDbContext db, Person person)
    {
        var cp = await db.CustomersProviders.FirstOrDefaultAsync(c => c.PersonId == person.Id);
        if (cp is null)
        {
            cp = new CustomersProvider { PersonId = person.Id };
            db.CustomersProviders.Add(cp);
        }

        cp.Name = string.IsNullOrWhiteSpace(person.Title)
            ? (person.FirstName + " " + person.LastName).Trim()
            : $"{person.Title} {person.FirstName} {person.LastName}".Trim();
        cp.Code = string.IsNullOrWhiteSpace(person.Code) ? $"P-{person.Id:0000}" : person.Code;
        cp.Description = person.Deleted ? "حذف شده — شخص" : cp.Description;

        // Deleting the person soft-deletes the mirror — unless invoices/detail accounts
        // still reference it (e.g. sold to this customer before); then it stays alive.
        bool deleted = person.Deleted;
        if (deleted && await IsReferencedAsync(db, cp.Id)) deleted = false;
        cp.Deleted = deleted;
        cp.UpdateDateTime = DateTime.Now;
        await db.SaveChangesAsync();
    }

    /// <summary>Call after a company is saved or soft-deleted.</summary>
    public static async Task SyncCompanyAsync(SadrDbContext db, Company company)
    {
        var cp = await db.CustomersProviders.FirstOrDefaultAsync(c => c.CompanyId == company.Id);
        if (cp is null)
        {
            cp = new CustomersProvider { CompanyId = company.Id };
            db.CustomersProviders.Add(cp);
        }

        cp.Name = company.FullName.Trim();
        cp.Code = string.IsNullOrWhiteSpace(company.Code) ? $"C-{company.Id:0000}" : company.Code;
        cp.Description = company.Deleted ? "حذف شده — شرکت" : cp.Description;

        bool deleted = company.Deleted;
        if (deleted && await IsReferencedAsync(db, cp.Id)) deleted = false;
        cp.Deleted = deleted;
        cp.UpdateDateTime = DateTime.Now;
        await db.SaveChangesAsync();
    }

    /// <summary>One-time backfill: creates mirrored rows for people/companies that lack one.</summary>
    public static async Task BackfillAsync(SadrDbContext db)
    {
        var people = await db.People.Where(p => !p.Deleted).ToListAsync();
        foreach (var p in people)
            if (!await db.CustomersProviders.AnyAsync(c => c.PersonId == p.Id))
                await SyncPersonAsync(db, p);

        var companies = await db.Companies.Where(c => !c.Deleted).ToListAsync();
        foreach (var c in companies)
            if (!await db.CustomersProviders.AnyAsync(x => x.CompanyId == c.Id))
                await SyncCompanyAsync(db, c);
    }

    private static async Task<bool> IsReferencedAsync(SadrDbContext db, int cpId) =>
        await db.Invoices.AnyAsync(i => !i.Deleted && (i.CustomerId == cpId || i.ProviderId == cpId))
        || await db.DetailAccounts.AnyAsync(d => !d.Deleted && d.CustomerProviderId == cpId);
}
