using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using DeskCycle.Core.Tracking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DeskCycle.Core.Tests;

/// <summary>
/// The recorder decides what ends up in the database. Its arithmetic on the raw
/// counter is where a mistake stays invisible the longest: the numbers still
/// look plausible, they are just wrong.
///
/// Runs against a real SQLite database in memory rather than against a mock --
/// the point is what gets stored.
/// </summary>
public class SessionRecorderTests : IDisposable
{
    private const int IdleTimeoutSeconds = 90;
    private const int MinimumRevolutions = 10;

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly TestClock _clock;
    private readonly LiveStatusService _live;
    private readonly SessionRecorder _recorder;

    public SessionRecorderTests()
    {
        // Stays alive as long as the connection does, so it has to be held open
        // for the whole test.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TrackerDbContext>(options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TrackerDbContext>().Database.EnsureCreated();
        }

        _clock = new TestClock(new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero));
        _live = new LiveStatusService();

        _recorder = new SessionRecorder(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            // Fully qualified: DeskCycle.Core.Options is a namespace here.
            Microsoft.Extensions.Options.Options.Create(new TrackingOptions
            {
                SessionIdleTimeoutSeconds = IdleTimeoutSeconds,
                MinimumSessionRevolutions = MinimumRevolutions,
            }),
            _live,
            _clock,
            NullLogger<SessionRecorder>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------ the counter

    /// <summary>
    /// After connecting it is unknown how far the Pico has counted in the
    /// meantime. Crediting that difference would invent a ride.
    /// </summary>
    [Fact]
    public async Task First_reading_after_connecting_only_sets_the_reference_point()
    {
        _recorder.OnSourceConnected();
        await ReadAsync(count: 5000, rpm: 60);

        Assert.Empty(await SessionsAsync());
        Assert.Equal(0, _live.Current.RevolutionDelta);
    }

    [Fact]
    public async Task Counts_the_difference_between_two_readings()
    {
        await RideAsync(from: 100, to: 112);

        var session = Assert.Single(await SessionsAsync());
        Assert.Equal(12, session.Revolutions);
    }

    /// <summary>
    /// After a restart the Pico counts from 0 again. Without this the difference
    /// would go negative -- and a session would lose everything it had.
    /// </summary>
    [Fact]
    public async Task Treats_a_falling_counter_as_a_restart_of_the_pico()
    {
        await RideAsync(from: 100, to: 120);

        // The Pico restarts and reports 4 revolutions since it came up.
        await ReadAsync(count: 4, rpm: 60);

        var session = Assert.Single(await SessionsAsync());
        Assert.Equal(24, session.Revolutions);
    }

    // ------------------------------------------------------------ sessions

    [Fact]
    public async Task Starts_a_session_with_the_first_revolution()
    {
        _recorder.OnSourceConnected();
        await ReadAsync(count: 100, rpm: 0);

        Assert.Empty(await SessionsAsync());

        await ReadAsync(count: 101, rpm: 60);

        var session = Assert.Single(await SessionsAsync());
        Assert.Null(session.EndedAt);
        Assert.True(_live.Current.SessionActive);
    }

    /// <summary>
    /// The end is the last revolution, not the moment the idle timeout expired.
    /// Otherwise every session would carry the idle time along.
    /// </summary>
    [Fact]
    public async Task Ends_the_session_at_the_last_revolution_after_the_idle_timeout()
    {
        await RideAsync(from: 100, to: 120);
        var lastMovement = _clock.Now;

        _clock.Advance(TimeSpan.FromSeconds(IdleTimeoutSeconds + 1));
        await ReadAsync(count: 120, rpm: 0);

        var session = Assert.Single(await SessionsAsync());
        Assert.Equal(lastMovement, session.EndedAt);
        Assert.Equal(20, session.Revolutions);
    }

    [Fact]
    public async Task Discards_a_session_that_stayed_below_the_minimum()
    {
        await RideAsync(from: 100, to: 100 + MinimumRevolutions - 1);

        _clock.Advance(TimeSpan.FromSeconds(IdleTimeoutSeconds + 1));
        await ReadAsync(count: 100 + MinimumRevolutions - 1, rpm: 0);

        Assert.Empty(await SessionsAsync());
    }

    [Fact]
    public async Task Keeps_a_session_that_reached_the_minimum()
    {
        await RideAsync(from: 100, to: 100 + MinimumRevolutions);

        _clock.Advance(TimeSpan.FromSeconds(IdleTimeoutSeconds + 1));
        await ReadAsync(count: 100 + MinimumRevolutions, rpm: 0);

        Assert.Single(await SessionsAsync());
    }

    /// <summary>Standstill is not recorded -- it added two thirds of the rows and nothing else.</summary>
    [Fact]
    public async Task Stores_samples_only_while_something_is_turning()
    {
        await RideAsync(from: 100, to: 110);
        await ReadAsync(count: 110, rpm: 0);

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        Assert.All(await db.Samples.ToListAsync(), sample => Assert.True(sample.Rpm > 0));
    }

    // ------------------------------------------------------------ live status

    [Fact]
    public async Task Publishes_which_kind_of_link_is_counting()
    {
        _recorder.OnSourceConnected();
        await ReadAsync(count: 100, rpm: 60, CadenceSourceKind.Bluetooth, "DeskCycle");

        Assert.True(_live.Current.SensorConnected);
        Assert.Equal(CadenceSourceKind.Bluetooth, _live.Current.SourceKind);
        Assert.Equal("DeskCycle", _live.Current.SourceName);
    }

    [Fact]
    public void Publishes_no_kind_at_all_while_nothing_is_connected()
    {
        _recorder.OnSourceUnavailable();

        Assert.False(_live.Current.SensorConnected);
        Assert.Equal(CadenceSourceKind.None, _live.Current.SourceKind);
        Assert.Null(_live.Current.SourceName);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>One reading per second, from the reference point up to the target count.</summary>
    private async Task RideAsync(int from, int to)
    {
        _recorder.OnSourceConnected();
        await ReadAsync(from, rpm: 0);

        for (var count = from + 1; count <= to; count++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            await ReadAsync(count, rpm: 60);
        }
    }

    private Task ReadAsync(
        int count,
        double rpm,
        CadenceSourceKind kind = CadenceSourceKind.Usb,
        string sourceName = "COM3") =>
        _recorder.OnReadingAsync(
            new CadenceReading(count, rpm, Bounce: 0, Suspect: 0), kind, sourceName, CancellationToken.None);

    private async Task<List<Session>> SessionsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        return await db.Sessions.AsNoTracking().ToListAsync();
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }
}
