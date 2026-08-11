using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetworkMonitor.Services.Data
{
    public class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            DbContextOptionsBuilder<AppDbContext> builder = new DbContextOptionsBuilder<AppDbContext>();

            builder.UseSqlite($"Data Source={AppDbContext.DbPath}");

            AppDbContext context = new AppDbContext(builder.Options);

            return context;
        }
    }
}
