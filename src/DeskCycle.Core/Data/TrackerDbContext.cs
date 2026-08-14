using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DeskCycle.Core.Data;

public class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<SessionSample> Samples => Set<SessionSample>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite can neither sort nor compare DateTimeOffset values stored as
        // text -- every query with an OrderBy or a time filter throws at runtime
        // otherwise. The converter stores a number instead.
        //
        // Sorting that number matches chronological order as long as all values
        // share the same UTC offset. They do here: every timestamp comes from
        // TimeProvider.GetUtcNow().
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Session>()
            .HasIndex(s => s.StartedAt);

        modelBuilder.Entity<SessionSample>()
            .HasIndex(s => new { s.SessionId, s.Timestamp });

        modelBuilder.Entity<SessionSample>()
            .HasOne(s => s.Session)
            .WithMany(s => s.Samples)
            .HasForeignKey(s => s.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
