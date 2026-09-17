using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>A print layout choice shown in the picker: the DB row plus its id.</summary>
public sealed class PrintSettingInfo
{
    public int Id;
    public string Name = "";
    public int Layout;
    public bool IsDefault;
    public string? TitleOverride;
    public string? FooterNote;
    public bool ShowDiscountColumn = true;
    public byte[]? Logo;

    public string Display => (IsDefault ? "⭐ " : "") + Name;

    // ToString drives ComboBox/DisplayMemberPath display and UIA item names.
    public override string ToString() => Display;
}

/// <summary>
/// The InvoicePrintSettings table is app-owned (not part of the original schema), so it is
/// created on demand with plain T-SQL the first time it is needed. Three predefined
/// layouts are seeded once; the first one becomes the default.
/// </summary>
public static class PrintSettingsService
{
    /// <summary>Creates InvoicePrintSettings when missing and seeds the 3 predefined layouts.</summary>
    public static void EnsureTableAndSeed(SadrDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            IF OBJECT_ID(N'dbo.InvoicePrintSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[InvoicePrintSettings](
                    [Id] INT IDENTITY(1,1) NOT NULL,
                    [Name] NVARCHAR(100) NOT NULL,
                    [Layout] INT NOT NULL,
                    [IsDefault] BIT NOT NULL,
                    [TitleOverride] NVARCHAR(100) NULL,
                    [FooterNote] NVARCHAR(500) NULL,
                    [ShowDiscountColumn] BIT NOT NULL,
                    [Logo] VARBINARY(MAX) NULL,
                    [Deleted] BIT NOT NULL,
                    [RecordUniqueId] UNIQUEIDENTIFIER NOT NULL,
                    [CreateUserId] UNIQUEIDENTIFIER NULL,
                    [UpdateUserId] UNIQUEIDENTIFIER NULL,
                    [CreateDateTime] DATETIME NULL,
                    [UpdateDateTime] DATETIME NULL,
                    CONSTRAINT [PK_InvoicePrintSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
                );
            END
            """);

        // Price table default-discount column (added after the table shipped).
        db.Database.ExecuteSqlRaw("""
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Price') AND name = N'DefaultDiscountPersentage')
                ALTER TABLE [dbo].[Price] ADD [DefaultDiscountPersentage] FLOAT NOT NULL DEFAULT 0;
            """);

        if (!db.InvoicePrintSettings.Any(s => !s.Deleted))
        {
            foreach (var layout in new[] { InvoiceLayoutConsts.Classic, InvoiceLayoutConsts.Modern, InvoiceLayoutConsts.Compact })
            {
                db.InvoicePrintSettings.Add(new InvoicePrintSetting
                {
                    Name = InvoiceLayoutConsts.Label(layout),
                    Layout = layout,
                    IsDefault = layout == InvoiceLayoutConsts.Classic,
                    ShowDiscountColumn = true
                });
            }
            db.SaveChanges();
        }
    }

    public static List<PrintSettingInfo> LoadList(SadrDbContext db)
    {
        EnsureTableAndSeed(db);
        return db.InvoicePrintSettings.AsNoTracking()
            .Where(s => !s.Deleted)
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Id)
            .Select(s => new PrintSettingInfo
            {
                Id = s.Id, Name = s.Name, Layout = s.Layout, IsDefault = s.IsDefault,
                TitleOverride = s.TitleOverride, FooterNote = s.FooterNote,
                ShowDiscountColumn = s.ShowDiscountColumn, Logo = s.Logo
            })
            .ToList();
    }

    /// <summary>Loads the default setting (or null when the table has none — caller falls back).</summary>
    public static PrintSettingInfo? LoadDefault(SadrDbContext db)
    {
        EnsureTableAndSeed(db);
        return db.InvoicePrintSettings.AsNoTracking()
            .Where(s => !s.Deleted)
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Id)
            .Select(s => new PrintSettingInfo
            {
                Id = s.Id, Name = s.Name, Layout = s.Layout, IsDefault = s.IsDefault,
                TitleOverride = s.TitleOverride, FooterNote = s.FooterNote,
                ShowDiscountColumn = s.ShowDiscountColumn, Logo = s.Logo
            })
            .FirstOrDefault();
    }

    /// <summary>Creates a new setting row (audit columns stamped by SaveChanges).</summary>
    public static int Create(SadrDbContext db, PrintSettingInfo info, byte[]? logo)
    {
        var row = new InvoicePrintSetting
        {
            Name = info.Name, Layout = info.Layout, IsDefault = false,
            TitleOverride = NullIfEmpty(info.TitleOverride),
            FooterNote = NullIfEmpty(info.FooterNote),
            ShowDiscountColumn = info.ShowDiscountColumn, Logo = logo
        };
        db.InvoicePrintSettings.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    public static void Update(SadrDbContext db, int id, PrintSettingInfo info, byte[]? logo, bool logoChanged)
    {
        var row = db.InvoicePrintSettings.First(s => s.Id == id);
        row.Name = info.Name;
        row.Layout = info.Layout;
        row.TitleOverride = NullIfEmpty(info.TitleOverride);
        row.FooterNote = NullIfEmpty(info.FooterNote);
        row.ShowDiscountColumn = info.ShowDiscountColumn;
        if (logoChanged) row.Logo = logo;
        db.SaveChanges();
    }

    public static void Delete(SadrDbContext db, int id)
    {
        var row = db.InvoicePrintSettings.First(s => s.Id == id);
        bool wasDefault = row.IsDefault;
        row.Deleted = true;
        row.IsDefault = false;
        db.SaveChanges();

        if (wasDefault)
        {
            var next = db.InvoicePrintSettings.Where(s => !s.Deleted).OrderBy(s => s.Id).FirstOrDefault();
            if (next is not null) { next.IsDefault = true; db.SaveChanges(); }
        }
    }

    public static void SetDefault(SadrDbContext db, int id)
    {
        foreach (var s in db.InvoicePrintSettings.Where(s => !s.Deleted))
            s.IsDefault = s.Id == id;
        db.SaveChanges();
    }

    /// <summary>Loads the logo file bytes for storing (png/jpg only, max 2 MB).</summary>
    public static byte[]? ReadLogoFile(string path, out string error)
    {
        error = "";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
        {
            error = "فقط فایل PNG یا JPG قابل قبول است.";
            return null;
        }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 2 * 1024 * 1024)
        {
            error = "حجم لوگو باید کمتر از ۲ مگابایت باشد.";
            return null;
        }
        return bytes;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
