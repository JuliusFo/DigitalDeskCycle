using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeskCycle.Core.Data;

/// <summary>
/// Used exclusively by "dotnet ef" when creating migrations.
///
/// Without this factory the tool would have to spin up the WPF application to
/// get hold of a DbContext -- serial port, window and all. The connection string
/// is a placeholder: creating a migration opens no database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TrackerDbContext>()
            .UseSqlite("Data Source=deskcycle-design.db")
            .Options;

        return new TrackerDbContext(options);
    }
}
