using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LegacyModernizationAgent.Infrastructure.Data;

/// <summary>
/// 'dotnet ef' komutları için design-time fabrika. Bu sayede migration üretirken
/// API host'u (ve onunla birlikte MSBuildLocator) ayağa kalkmaz.
///   dotnet ef migrations add Ad --project src/LegacyModernizationAgent.Infrastructure
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=data/legacymod.db")
            .Options;

        return new AppDbContext(options);
    }
}
