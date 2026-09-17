using Microsoft.EntityFrameworkCore;

namespace SadrApp.Data;

public static class SadrDb
{
    private static readonly object Lock = new();

    public static SadrDbContext New()
    {
        lock (Lock)
        {
            var builder = new DbContextOptionsBuilder<SadrDbContext>();
            builder.UseSqlServer(SadrApp.Infrastructure.Database.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(2);
                sql.CommandTimeout(0);
            });
            return new SadrDbContext(builder.Options);
        }
    }
}
