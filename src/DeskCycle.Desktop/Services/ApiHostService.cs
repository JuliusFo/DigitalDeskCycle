using DeskCycle.Core.Data;
using DeskCycle.Core.Options;
using DeskCycle.Core.Tracking;
using DeskCycle.Desktop.Api;
using DeskCycle.Desktop.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeskCycle.Desktop.Services;

/// <summary>
/// Starts and stops the embedded web server at runtime.
///
/// While it is off it does not exist -- no socket, no listener, no firewall
/// prompt. While it runs it costs practically nothing when idle.
///
/// The web server gets its own DI container but shares the
/// <see cref="LiveStatusService"/> instance with the application -- otherwise it
/// would keep a second, silent state of its own. The database context is
/// short-lived anyway and merely opens the same file.
/// </summary>
public sealed class ApiHostService(
    LiveStatusService live,
    IOptions<TrackingOptions> trackingOptions,
    UserSettingsStore settings,
    ILogger<ApiHostService> logger)
{
    private WebApplication? _app;

    public bool IsRunning => _app is not null;

    public string Url
    {
        get
        {
            var current = settings.Current;
            var host = current.ApiAllowRemote ? Environment.MachineName : "localhost";
            return $"http://{host}:{current.ApiPort}";
        }
    }

    public async Task<bool> StartAsync()
    {
        if (_app is not null)
        {
            return true;
        }

        var current = settings.Current;

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
            });

            // "localhost" rather than "*": without an explicit opt-in the server
            // stays on this machine and Windows never asks in the first place.
            var binding = current.ApiAllowRemote ? "*" : "localhost";
            builder.WebHost.UseUrls($"http://{binding}:{current.ApiPort}");

            builder.Services.AddSingleton(live);
            builder.Services.AddSingleton(trackingOptions);
            builder.Services.AddDbContext<TrackerDbContext>(o => o.UseSqlite(AppPaths.ConnectionString));
            builder.Services.AddSignalR();
            builder.Services.AddHostedService<SignalRLiveBridge>();

            var app = builder.Build();

            // Without a console there would otherwise be no hint at all about why
            // a request failed -- just an empty 500.
            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.MapHub<CadenceHub>("/hubs/cadence");
            app.MapTrackingApi();

            await app.StartAsync();
            _app = app;

            logger.LogInformation("Data sharing active on {Url}.", Url);
            return true;
        }
        catch (Exception ex)
        {
            // Most common cause: the port is taken.
            logger.LogError(ex, "The web server could not be started.");
            _app = null;
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        var app = _app;
        _app = null;

        try
        {
            await app.StopAsync();
            await app.DisposeAsync();
            logger.LogInformation("Data sharing stopped.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The web server did not shut down cleanly.");
        }
    }
}
