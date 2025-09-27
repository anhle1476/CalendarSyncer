using CalendarSync;
using CalendarSync.Models;
using Serilog;

// Configure Serilog for file logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/calendar-sync-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting CalendarSyncService");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure to run as Windows Service
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "CalendarSyncService";
    });

    // Add Serilog
    builder.Services.AddSerilog();

    // Configure strongly typed settings
    builder.Services.Configure<GoogleSettings>(
        builder.Configuration.GetSection("Google"));
    builder.Services.Configure<DatabaseSettings>(
        builder.Configuration.GetSection("Database"));
    builder.Services.Configure<SyncSettings>(
        builder.Configuration.GetSection("Sync"));
    builder.Services.Configure<NotificationSettings>(
        builder.Configuration.GetSection("Notification"));

    // Register the Worker service
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
