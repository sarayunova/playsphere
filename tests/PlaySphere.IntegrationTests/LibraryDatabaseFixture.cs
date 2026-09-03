using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Library;
using PlaySphere.Infrastructure.Library;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Spins up a real (temp-file) SQLite database with migrations applied, wired through the same
/// <c>AddPlaySphereLibrary</c>/<c>MigrateLibraryDatabase</c> path the app uses at startup — not a
/// hand-rolled substitute — so these tests exercise the actual persistence stack.
/// </summary>
public sealed class LibraryDatabaseFixture : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _dbDirectory;

    public LibraryDatabaseFixture()
    {
        _dbDirectory = Path.Combine(Path.GetTempPath(), "PlaySphereLibraryTests_" + Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(_dbDirectory, "library.db");

        _services = new ServiceCollection()
            .AddPlaySphereLibrary(dbPath)
            .BuildServiceProvider();

        _services.MigrateLibraryDatabase();
    }

    public IMediaLibraryRepository Repository => _services.GetRequiredService<IMediaLibraryRepository>();

    public void Dispose()
    {
        _services.Dispose();

        // Microsoft.Data.Sqlite pools native connections by connection string; without clearing
        // the pool, the file stays locked briefly after disposal and deleting it here would race.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_dbDirectory))
        {
            Directory.Delete(_dbDirectory, recursive: true);
        }
    }
}
