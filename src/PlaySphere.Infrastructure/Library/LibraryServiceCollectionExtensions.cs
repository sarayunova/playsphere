using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Library;

namespace PlaySphere.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite-backed library store: an <see cref="IDbContextFactory{TContext}"/>
    /// for <see cref="PlaySphereDbContext"/> pointed at <paramref name="databasePath"/>, and
    /// <see cref="IMediaLibraryRepository"/>. Does not apply migrations — call
    /// <see cref="MigrateLibraryDatabase"/> once after the container is built.
    /// </summary>
    public static IServiceCollection AddPlaySphereLibrary(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContextFactory<PlaySphereDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<IMediaLibraryRepository, EfMediaLibraryRepository>();

        return services;
    }

    /// <summary>Applies pending EF Core migrations, creating the database file if it doesn't exist yet.</summary>
    public static void MigrateLibraryDatabase(this IServiceProvider services)
    {
        using var db = services.GetRequiredService<IDbContextFactory<PlaySphereDbContext>>().CreateDbContext();
        db.Database.Migrate();
    }
}
