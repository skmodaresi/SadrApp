using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SadrApp.Data;

// ===================== Lookups =====================

[Table("ProductCategories")]
public class ProductCategory
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ParentId))]
    public virtual ProductCategory? Parent { get; set; }
    public virtual ICollection<ProductCategory> Children { get; set; } = new List<ProductCategory>();
}

[Table("ProjectCategories")]
public class ProjectCategory
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Description { get; set; }
    [Column("ParentCategoryId")] public int? ParentId { get; set; }
    [Column("UpdateUseId")] public Guid? UpdateUserIdLegacy { get; set; }
    public Guid? UpdateUserId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ParentId))]
    public virtual ProjectCategory? Parent { get; set; }
    public virtual ICollection<ProjectCategory> Children { get; set; } = new List<ProjectCategory>();
}

[Table("Brands")]
public class Brand
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

[Table("Units")]
public class Unit
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ParentUnitId { get; set; }
    public decimal ParentPercentRel { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ParentUnitId))]
    public virtual Unit? ParentUnit { get; set; }
}

[Table("Currencies")]
public class Currency
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

[Table("Roles")]
public class Role
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

// ===================== Products =====================

[Table("Products")]
public class Product
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    [Column("MainCategoryId")] public int MainCategoryId { get; set; }
    [Column("MainUnitId")] public int MainUnitId { get; set; }
    public int? BrandId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(MainCategoryId))] public virtual ProductCategory? MainCategory { get; set; }
    [ForeignKey(nameof(MainUnitId))] public virtual Unit? MainUnit { get; set; }
    [ForeignKey(nameof(BrandId))] public virtual Brand? Brand { get; set; }
    public virtual ICollection<ProductExtraCategory> ExtraCategories { get; set; } = new List<ProductExtraCategory>();
    public virtual ICollection<ProductAttrib> Attribs { get; set; } = new List<ProductAttrib>();
}

[Table("ProductExtraCategories")]
public class ProductExtraCategory
{
    [Key] public int Id { get; set; }
    public int ProductId { get; set; }
    [Column("CategoryId")] public int CategoryId { get; set; }
    [Column("Descriptionn")] public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ProductId))] public virtual Product? Product { get; set; }
    [ForeignKey(nameof(CategoryId))] public virtual ProductCategory? Category { get; set; }
}

// ===================== Projects =====================

[Table("Projects")]
public class Project
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    [Column("Descrioption")] public string Description { get; set; } = "";
    [Column("Code")] public string Code { get; set; } = "";
    public int ProjectType { get; set; }
    public string StartDate { get; set; } = "";
    public DateTime StartDateG { get; set; }
    public string EndDate { get; set; } = "";
    public DateTime EndDateG { get; set; }
    public decimal ProjectPrice { get; set; }
    public decimal BaseBudget { get; set; }
    public decimal? FinalSpendBudget { get; set; }
    public decimal? CurrentSpendBudget { get; set; }
    public int CurrencyId { get; set; }
    public int Status { get; set; }
    public int? ParentProjectId { get; set; }
    public int CategoryId { get; set; }
    public int CurrentProgressPercentage { get; set; }
    [Column("ProjetcCreatorPersonId")] public int CreatorPersonId { get; set; }
    public int ProjectManagerPersonId { get; set; }
    public Guid GUID { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(CategoryId))] public virtual ProjectCategory? Category { get; set; }
    [ForeignKey(nameof(CurrencyId))] public virtual Currency? Currency { get; set; }
    [ForeignKey(nameof(ParentProjectId))] public virtual Project? ParentProject { get; set; }
    [ForeignKey(nameof(CreatorPersonId))] public virtual Person? CreatorPerson { get; set; }
    [ForeignKey(nameof(ProjectManagerPersonId))] public virtual Person? ManagerPerson { get; set; }
    public virtual List<ProjectTask> Tasks { get; set; } = new();
}

// ===================== People / Company / User =====================

[Table("People")]
public class Person
{
    [Key] public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = "";
    public string? Code { get; set; }
    public string? Title { get; set; }
    public string? FatherName { get; set; }
    public int? Gender { get; set; }
    public string? BirthDate { get; set; }
    public string? CertNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? IDInformation { get; set; }
    public string? SystemData { get; set; }
    public int? PictureFileId { get; set; }
    public bool? IsActive { get; set; }
    public Guid? LoginUserId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }

    [ForeignKey(nameof(LoginUserId))] public virtual User? LoginUser { get; set; }
}

[Table("Companies")]
public class Company
{
    [Key] public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Code { get; set; } = "";
    public string NationalId { get; set; } = "";
    [Column("RegisterId")] public string RegisterId { get; set; } = "";
    public string Address { get; set; } = "";
    public string Tel { get; set; } = "";
    public string? Email { get; set; }
    public int? ManagerId { get; set; }
    public int? PeoplesId { get; set; }
    public Guid GUID { get; set; }
    public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ManagerId))] public virtual Person? Manager { get; set; }
}

[Table("Users")]
public class User
{
    [Key] public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public bool EmailConfirmed { get; set; }
}

// ===================== Banking =====================

[Table("Banks")]
public class Bank
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
    public virtual ICollection<BankBranch> Branches { get; set; } = new List<BankBranch>();
}

[Table("BankBranches")]
public class BankBranch
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? Tel { get; set; }
    public int BankId { get; set; }
    public int? ManagerPersonId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(BankId))] public virtual Bank? Bank { get; set; }
    [ForeignKey(nameof(ManagerPersonId))] public virtual Person? Manager { get; set; }
}

[Table("BankAccounts")]
public class BankAccount
{
    [Key] public int Id { get; set; }
    public int BankBranchId { get; set; }
    public int? PersonId { get; set; }
    public int? CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string? CardNumber { get; set; }
    [Column("IDNumber")] public string IdNumber { get; set; } = "";
    [Column("SignPeople")] public string? SignPeople { get; set; }
    public int AccountType { get; set; }
    public string? Description { get; set; }
    public decimal StartBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public int CurrencyId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(BankBranchId))] public virtual BankBranch? Branch { get; set; }
    [ForeignKey(nameof(PersonId))] public virtual Person? Person { get; set; }
    [ForeignKey(nameof(CompanyId))] public virtual Company? Company { get; set; }
    [ForeignKey(nameof(CurrencyId))] public virtual Currency? Currency { get; set; }
}

// ===================== Warehouses =====================

// ===================== Product Attributes =====================

// ===================== Invoices =====================

public static class InvoiceTypeConsts
{
    public const int Sell = 0;
    public const int Buy = 1;
    public const int PreInvoice = 2;

    public static string Label(int t) => t switch
    {
        Sell => "فاکتور فروش",
        Buy => "فاکتور خرید",
        _ => "پیش‌فاکتور"
    };

    public static string Icon(int t) => t switch
    {
        Sell => "🧾",
        Buy => "🛒",
        _ => "📄"
    };
}

/// <summary>Which of the three predefined invoice print layouts a setting row uses.</summary>
public static class InvoiceLayoutConsts
{
    public const int Classic = 0;
    public const int Modern = 1;
    public const int Compact = 2;

    public static string Label(int l) => l switch
    {
        Modern => "ساده و مدرن",
        Compact => "کامپکت",
        _ => "کلاسیک رسمی"
    };
}

public static class InvoiceStatusConsts
{
    public const int Draft = 0;
    public const int Issued = 1;
    public const int Confirmed = 2;
    public const int Cancelled = 3;

    public static string Label(int s) => s switch
    {
        Draft => "پیش‌نویس",
        Issued => "صادر شده",
        Confirmed => "تأیید شده",
        _ => "لغو شده"
    };
}

[Table("Invoices")]
public class Invoice
{
    [Key] public int Id { get; set; }
    /// <summary>Persian yyyy/MM/dd display text, kept in sync with InvoiceDateG.</summary>
    public string InvoiceDate { get; set; } = "";
    public DateTime InvoiceDateG { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string? Code { get; set; }
    public string Description { get; set; } = "";
    public decimal TotalPrice { get; set; }
    public decimal TotalDiscount { get; set; }
    /// <summary>Customer for sell invoices / pre-invoices (FK CustomersProviders).</summary>
    public int? CustomerId { get; set; }
    /// <summary>Supplier for buy invoices (FK CustomersProviders).</summary>
    public int? ProviderId { get; set; }
    public int InvoiceType { get; set; }
    public int Status { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(CustomerId))] public virtual CustomersProvider? Customer { get; set; }
    [ForeignKey(nameof(ProviderId))] public virtual CustomersProvider? Provider { get; set; }
    public virtual ICollection<InvoiceDetail> Details { get; set; } = new List<InvoiceDetail>();
}

[Table("InvoiceDetails")]
public class InvoiceDetail
{
    [Key] public int Id { get; set; }
    public int InvoiceId { get; set; }
    public int ProductId { get; set; }
    /// <summary>Optional link to the Price row the unit price came from.</summary>
    public int? PriceId { get; set; }
    public decimal? UnitPriceAmount { get; set; }
    public int? CurrencyId { get; set; }
    public int UnitId { get; set; }
    public decimal Amount { get; set; }
    public double? ExtraPersentage { get; set; }
    public double? DiscountPersentage { get; set; }
    public int Status { get; set; }
    public decimal? TotalDiscount { get; set; }
    public decimal TotalPriceAmount { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(InvoiceId))] public virtual Invoice? Invoice { get; set; }
    [ForeignKey(nameof(ProductId))] public virtual Product? Product { get; set; }
    [ForeignKey(nameof(PriceId))] public virtual Price? Price { get; set; }
    [ForeignKey(nameof(CurrencyId))] public virtual Currency? Currency { get; set; }
    [ForeignKey(nameof(UnitId))] public virtual Unit? Unit { get; set; }
}

[Table("Price")]
public class Price
{
    [Key] public int Id { get; set; }
    public int ProductId { get; set; }
    public int UnitId { get; set; }
    public int CurrencyId { get; set; }
    public decimal PriceAmount { get; set; }
    /// <summary>Default row discount (٪) suggested in the invoice editor when this price is picked.</summary>
    public double DefaultDiscountPersentage { get; set; }
    public string SetDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public DateTime SetDateG { get; set; }
    public DateTime EndDateG { get; set; }
    public int Status { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ProductId))] public virtual Product? Product { get; set; }
    [ForeignKey(nameof(UnitId))] public virtual Unit? Unit { get; set; }
    [ForeignKey(nameof(CurrencyId))] public virtual Currency? Currency { get; set; }
}

/// <summary>Constants for the attribute tables.</summary>
public static class AttribConsts
{
    /// <summary>Prefix stored in CategoryAttributes.Description marking links that were
    /// auto-created for a product-level extra attribute (not a manual category link).</summary>
    public const string AutoProductLink = "##PRODUCT-LINK##";
}

[Table("Attributes")]
public class AttributeDef
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

[Table("CategoryAttributes")]
public class CategoryAttribute
{
    [Key] public int Id { get; set; }
    public int CategoryId { get; set; }
    public int AttributeId { get; set; }
    public string Description { get; set; } = "";
    public bool IsForced { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(CategoryId))] public virtual ProductCategory? Category { get; set; }
    [ForeignKey(nameof(AttributeId))] public virtual AttributeDef? Attribute { get; set; }
}

[Table("ProductAttrib")]
public class ProductAttrib
{
    [Key] public int Id { get; set; }
    public int ProductId { get; set; }
    public int AttribId { get; set; }
    public int CategoryAttribId { get; set; }
    public string Value { get; set; } = "";
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ProductId))] public virtual Product? Product { get; set; }
    [ForeignKey(nameof(AttribId))] public virtual AttributeDef? Attribute { get; set; }
    [ForeignKey(nameof(CategoryAttribId))] public virtual CategoryAttribute? CategoryAttribute { get; set; }
}

// ===================== Accounting =====================

public static class AccountTypeConsts
{
    public const int Debit = 0;
    public const int Credit = 1;
    public const int Both = 2;

    public static string Label(int t) => t switch
    {
        Debit => "بدهکار",
        Credit => "بستانکار",
        _ => "جهت‌دار (هر دو)"
    };
}

[Table("AccountGroups")]
public class AccountGroup
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public int AccountType { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

[Table("GeneralAccounts")]
public class GeneralAccount
{
    [Key] public int Id { get; set; }
    [Column("AcountGroupId")] public int AccountGroupId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public int AccountType { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(AccountGroupId))] public virtual AccountGroup? Group { get; set; }
}

[Table("SubSidiaryAccounts")]
public class SubSidiaryAccount
{
    [Key] public int Id { get; set; }
    public int GeneralAccountId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public int AccountType { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(GeneralAccountId))] public virtual GeneralAccount? General { get; set; }
}

[Table("DetailAccounts")]
public class DetailAccount
{
    [Key] public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int SubSidiaryAccountId { get; set; }
    public int AccountType { get; set; }
    public int? CustomerProviderId { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(SubSidiaryAccountId))] public virtual SubSidiaryAccount? SubSidiary { get; set; }
    [ForeignKey(nameof(CustomerProviderId))] public virtual CustomersProvider? CustomerProvider { get; set; }
}

[Table("CustomersProviders")]
public class CustomersProvider
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    /// <summary>Set on auto rows created from a Person (mirrored name/code).</summary>
    public int? PersonId { get; set; }
    /// <summary>Set on auto rows created from a Company (mirrored name/code).</summary>
    public int? CompanyId { get; set; }
    public string? Description { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(PersonId))] public virtual Person? Person { get; set; }
    [ForeignKey(nameof(CompanyId))] public virtual Company? Company { get; set; }
}

[Table("InvoicePrintSettings")]
public class InvoicePrintSetting
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Layout { get; set; }                     // InvoiceLayoutConsts
    public bool IsDefault { get; set; }
    public string? TitleOverride { get; set; }          // printed title instead of فاکتور فروش/...
    public string? FooterNote { get; set; }
    public bool ShowDiscountColumn { get; set; } = true;
    public byte[]? Logo { get; set; }                   // png/jpg bytes printed in the header
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}

// ===================== Project tasks =====================

[Table("ProjectTasks")]
public class ProjectTask
{
    [Key] public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string StartDate { get; set; } = "";
    public DateTime StartDateG { get; set; }
    public string DueDate { get; set; } = "";
    public DateTime DueDateG { get; set; }
    public int WorkingDays { get; set; }
    public int ResponcePersonId { get; set; }
    public string Description { get; set; } = "";
    public int ProgressPersentage { get; set; }
    public int ProjectProgressPercentage { get; set; }
    public string PreviousTasks { get; set; } = "";
    public decimal StartBudget { get; set; }
    public decimal? FinalBudget { get; set; }
    public string? DoneDescription { get; set; }
    public string? EndedDate { get; set; }
    public DateTime? EndedDateG { get; set; }
    public string? FailDescription { get; set; }
    public string? StartedDate { get; set; }
    public DateTime? StartedDateG { get; set; }
    public int Status { get; set; }
    public bool Closed { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(ProjectId))] public virtual Project? Project { get; set; }
    [ForeignKey(nameof(ResponcePersonId))] public virtual Person? ResponcePerson { get; set; }
    public virtual List<TaskReport> Reports { get; set; } = new();
}

[Table("TaskReports")]
public class TaskReport
{
    [Key] public int Id { get; set; }
    public int TaskId { get; set; }
    public int FreeTaskId { get; set; }
    public string ReportDate { get; set; } = "";
    public string Description { get; set; } = "";
    public int ReporterUserId { get; set; }
    public decimal Cost { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }

    [ForeignKey(nameof(TaskId))] public virtual ProjectTask? Task { get; set; }
}

public static class TaskStatusConsts
{
    public const int NotStarted = 0;
    public const int InProgress = 1;
    public const int Paused = 2;
    public const int Done = 3;
    public const int Failed = 4;

    public static string Label(int s) => s switch
    {
        NotStarted => "شروع نشده",
        InProgress => "در حال اجرا",
        Paused => "متوقف",
        Done => "انجام شده",
        Failed => "ناموفق",
        _ => "نامشخص"
    };
}

[Table("WareHouses")]
public class WareHouse
{
    [Key] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public string Tel { get; set; } = "";
    public bool Active { get; set; }
    public bool Deleted { get; set; }
    public Guid RecordUniqueId { get; set; }
    public Guid? CreateUserId { get; set; }
    public Guid? UpdateUserId { get; set; }
    public DateTime? CreateDateTime { get; set; }
    public DateTime? UpdateDateTime { get; set; }
}
