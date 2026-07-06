using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YGODuelSimulator.Data;

/// <summary>
/// Lets the EF Core CLI (dotnet ef migrations / database update) create a
/// context at design time without starting the WPF application.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(DatabaseConfig.GetConnectionString())
            .Options;
        return new AppDbContext(options);
    }
}
