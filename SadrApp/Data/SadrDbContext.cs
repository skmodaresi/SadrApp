using Microsoft.EntityFrameworkCore;

namespace SadrApp.Data;

public class SadrDbContext : DbContext
{
    public SadrDbContext(DbContextOptions<SadrDbContext> options) : base(options) { }

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductExtraCategory> ProductExtraCategories => Set<ProductExtraCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProjectCategory> ProjectCategories => Set<ProjectCategory>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<TaskReport> TaskReports => Set<TaskReport>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<BankBranch> BankBranches => Set<BankBranch>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<WareHouse> WareHouses => Set<WareHouse>();
    public DbSet<AttributeDef> Attributes => Set<AttributeDef>();
    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();
    public DbSet<ProductAttrib> ProductAttribs => Set<ProductAttrib>();
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();
    public DbSet<GeneralAccount> GeneralAccounts => Set<GeneralAccount>();
    public DbSet<SubSidiaryAccount> SubSidiaryAccounts => Set<SubSidiaryAccount>();
    public DbSet<DetailAccount> DetailAccounts => Set<DetailAccount>();
    public DbSet<CustomersProvider> CustomersProviders => Set<CustomersProvider>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceDetail> InvoiceDetails => Set<InvoiceDetail>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<InvoicePrintSetting> InvoicePrintSettings => Set<InvoicePrintSetting>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Self-referencing hierarchies: no cascade delete.
        mb.Entity<ProductCategory>().HasOne(c => c.Parent)
          .WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProjectCategory>().HasOne(c => c.Parent)
          .WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<Product>().HasOne(p => p.MainCategory)
          .WithMany().HasForeignKey(p => p.MainCategoryId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Product>().HasOne(p => p.MainUnit)
          .WithMany().HasForeignKey(p => p.MainUnitId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Product>().HasOne(p => p.Brand)
          .WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<ProductExtraCategory>().HasOne(e => e.Product)
          .WithMany(p => p.ExtraCategories).HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProductExtraCategory>().HasOne(e => e.Category)
          .WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<Project>().HasOne(p => p.Category)
          .WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Project>().HasOne(p => p.Currency)
          .WithMany().HasForeignKey(p => p.CurrencyId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Project>().HasOne(p => p.ParentProject)
          .WithMany().HasForeignKey(p => p.ParentProjectId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Project>().HasOne(p => p.CreatorPerson)
          .WithMany().HasForeignKey(p => p.CreatorPersonId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Project>().HasOne(p => p.ManagerPerson)
          .WithMany().HasForeignKey(p => p.ProjectManagerPersonId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<Person>().HasOne(p => p.LoginUser)
          .WithMany().HasForeignKey(p => p.LoginUserId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<Company>().HasOne(c => c.Manager)
          .WithMany().HasForeignKey(c => c.ManagerId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<BankBranch>().HasOne(b => b.Bank)
          .WithMany(bank => bank.Branches).HasForeignKey(b => b.BankId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<BankBranch>().HasOne(b => b.Manager)
          .WithMany().HasForeignKey(b => b.ManagerPersonId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<BankAccount>().HasOne(a => a.Branch)
          .WithMany().HasForeignKey(a => a.BankBranchId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<BankAccount>().HasOne(a => a.Person)
          .WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<BankAccount>().HasOne(a => a.Company)
          .WithMany().HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<BankAccount>().HasOne(a => a.Currency)
          .WithMany().HasForeignKey(a => a.CurrencyId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<Unit>().HasOne(u => u.ParentUnit)
          .WithMany().HasForeignKey(u => u.ParentUnitId).OnDelete(DeleteBehavior.NoAction);

        // Product attributes
        mb.Entity<CategoryAttribute>().HasOne(a => a.Category)
          .WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<CategoryAttribute>().HasOne(a => a.Attribute)
          .WithMany().HasForeignKey(a => a.AttributeId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProductAttrib>().HasOne(a => a.Product)
          .WithMany(p => p.Attribs).HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProductAttrib>().HasOne(a => a.Attribute)
          .WithMany().HasForeignKey(a => a.AttribId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProductAttrib>().HasOne(a => a.CategoryAttribute)
          .WithMany().HasForeignKey(a => a.CategoryAttribId).OnDelete(DeleteBehavior.NoAction);

        // Accounting chain
        mb.Entity<GeneralAccount>().HasOne(g => g.Group)
          .WithMany().HasForeignKey(g => g.AccountGroupId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<SubSidiaryAccount>().HasOne(s => s.General)
          .WithMany().HasForeignKey(s => s.GeneralAccountId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<DetailAccount>().HasOne(d => d.SubSidiary)
          .WithMany().HasForeignKey(d => d.SubSidiaryAccountId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<DetailAccount>().HasOne(d => d.CustomerProvider)
          .WithMany().HasForeignKey(d => d.CustomerProviderId).OnDelete(DeleteBehavior.NoAction);

        // Invoices
        mb.Entity<Invoice>().HasOne(i => i.Customer)
          .WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Invoice>().HasOne(i => i.Provider)
          .WithMany().HasForeignKey(i => i.ProviderId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<InvoiceDetail>().HasOne(d => d.Invoice)
          .WithMany(i => i.Details).HasForeignKey(d => d.InvoiceId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<InvoiceDetail>().HasOne(d => d.Product)
          .WithMany().HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<InvoiceDetail>().HasOne(d => d.Price)
          .WithMany().HasForeignKey(d => d.PriceId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<InvoiceDetail>().HasOne(d => d.Currency)
          .WithMany().HasForeignKey(d => d.CurrencyId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<InvoiceDetail>().HasOne(d => d.Unit)
          .WithMany().HasForeignKey(d => d.UnitId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Price>().HasOne(p => p.Product)
          .WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Price>().HasOne(p => p.Unit)
          .WithMany().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<Price>().HasOne(p => p.Currency)
          .WithMany().HasForeignKey(p => p.CurrencyId).OnDelete(DeleteBehavior.NoAction);

        mb.Entity<CustomersProvider>().HasOne(c => c.Person)
          .WithMany().HasForeignKey(c => c.PersonId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<CustomersProvider>().HasOne(c => c.Company)
          .WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.NoAction);

        // Project tasks
        mb.Entity<ProjectTask>().HasOne(t => t.Project)
          .WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<ProjectTask>().HasOne(t => t.ResponcePerson)
          .WithMany().HasForeignKey(t => t.ResponcePersonId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<TaskReport>().HasOne(r => r.Task)
          .WithMany(t => t.Reports).HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.NoAction);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fills CreateUserId/UpdateUserId/CreateDateTime/UpdateDateTime/RecordUniqueId on every
    /// audited entity automatically. Values already set by the caller are respected.
    /// </summary>
    private void StampAudit()
    {
        var now = DateTime.Now;
        var user = Infrastructure.UserSession.CurrentUserId;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Metadata.FindProperty("CreateDateTime") is null) continue; // table without audit columns

            if (entry.State == EntityState.Added)
            {
                if (entry.Metadata.FindProperty("RecordUniqueId") is not null &&
                    entry.Property("RecordUniqueId").CurrentValue is Guid g && g == Guid.Empty)
                    entry.Property("RecordUniqueId").CurrentValue = Guid.NewGuid();

                if (entry.Property("CreateDateTime").CurrentValue is null)
                    entry.Property("CreateDateTime").CurrentValue = now;

                if (entry.Metadata.FindProperty("CreateUserId") is not null &&
                    entry.Property("CreateUserId").CurrentValue is null)
                    entry.Property("CreateUserId").CurrentValue = user;
            }

            if (entry.Metadata.FindProperty("UpdateDateTime") is not null)
                entry.Property("UpdateDateTime").CurrentValue = now;

            if (entry.Metadata.FindProperty("UpdateUserId") is not null)
                entry.Property("UpdateUserId").CurrentValue = user;
        }
    }
}
