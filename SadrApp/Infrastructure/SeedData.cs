using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

public static class SeedData
{
    public static void Run()
    {
        using var db = SadrDb.New();

        if (!db.Currencies.Any(c => !c.Deleted))
        {
            db.Currencies.Add(new Currency
            {
                Name = "ریال", Code = "IRR", Description = "واحد پول پیش‌فرض",
                Deleted = false, RecordUniqueId = Guid.NewGuid(), CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
        }

        if (!db.Roles.Any(r => !r.Deleted))
        {
            db.Roles.Add(new Role
            {
                Name = "Admin", Description = "مدیر سیستم - دسترسی کامل",
                IsActive = true, Deleted = false, RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
            db.Roles.Add(new Role
            {
                Name = "User", Description = "کاربر عادی",
                IsActive = true, Deleted = false, RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
        }

        // No default user is seeded here: the first-run wizard (FirstRunSetupWindow)
        // asks for the main admin username/password on a fresh database.

        if (!db.Units.Any(u => !u.Deleted))
        {
            db.Units.Add(new Unit
            {
                Name = "عدد", Description = "واحد پایه شمارش",
                ParentPercentRel = 0, Deleted = false, RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
            db.Units.Add(new Unit
            {
                Name = "کیلوگرم", Description = "واحد وزن",
                ParentPercentRel = 0, Deleted = false, RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
            db.Units.Add(new Unit
            {
                Name = "متر", Description = "واحد طول",
                ParentPercentRel = 0, Deleted = false, RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = DateTime.Now, UpdateDateTime = DateTime.Now
            });
        }

        db.SaveChanges();
    }
}
