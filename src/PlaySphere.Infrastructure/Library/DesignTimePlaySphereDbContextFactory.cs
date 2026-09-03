using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PlaySphere.Infrastructure.Library;

/// <summary>
/// Lets <c>dotnet ef migrations</c> construct <see cref="PlaySphereDbContext"/> at design time,
/// without needing the app's DI container or a real database path. Not used at runtime.
/// </summary>
public sealed class DesignTimePlaySphereDbContextFactory : IDesignTimeDbContextFactory<PlaySphereDbContext>
{
    public PlaySphereDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlaySphereDbContext>()
            .UseSqlite("Data Source=design-time-only.db")
            .Options;

        return new PlaySphereDbContext(options);
    }
}
