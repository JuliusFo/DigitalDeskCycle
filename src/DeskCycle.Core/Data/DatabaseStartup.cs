using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DeskCycle.Core.Data;

public static class DatabaseStartup
{
    public static async Task PrepareAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        await BaselineIfCreatedWithoutMigrationsAsync(db);
        await db.Database.MigrateAsync();
        await CloseDanglingSessionsAsync(db);
    }

    /// <summary>
    /// Databases from before migrations existed were created with EnsureCreated
    /// and have no migration history. MigrateAsync would try to create the
    /// already existing tables there and fail.
    ///
    /// So the first migration is recorded as "already applied" without running
    /// it -- its schema is in place anyway. From then on everything follows the
    /// normal path, without losing a single training session.
    /// </summary>
    private static async Task BaselineIfCreatedWithoutMigrationsAsync(TrackerDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();

        // Fresh installation: MigrateAsync creates everything itself. An existing
        // but empty file falls under this too -- otherwise a stamp would be set
        // without the tables ever having been created.
        if (!await creator.ExistsAsync() || !await creator.HasTablesAsync())
        {
            return;
        }

        var history = db.GetService<IHistoryRepository>();
        if (await history.ExistsAsync())
        {
            return;
        }

        var initialMigration = db.Database.GetMigrations().FirstOrDefault();
        if (initialMigration is null)
        {
            return;
        }

        var efVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0";

        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        await db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigration, efVersion)));
    }

    /// <summary>
    /// Sessions left open by a crash or a power cut are closed at their last
    /// sample -- otherwise they run forever and skew every statistic.
    /// </summary>
    private static async Task CloseDanglingSessionsAsync(TrackerDbContext db)
    {
        var dangling = await db.Sessions.Where(s => s.EndedAt == null).ToListAsync();

        foreach (var session in dangling)
        {
            var lastSampleAt = await db.Samples
                .Where(s => s.SessionId == session.Id)
                .OrderByDescending(s => s.Timestamp)
                .Select(s => (DateTimeOffset?)s.Timestamp)
                .FirstOrDefaultAsync();

            session.EndedAt = lastSampleAt ?? session.StartedAt;
        }

        if (dangling.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }
}
