using CalendarSync;
using CalendarSync.Models;
using CalendarSync.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
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
    builder.Services.Configure<RabbitMQSettings>(
        builder.Configuration.GetSection("RabbitMQ"));
    builder.Services.Configure<WebhookSettings>(
        builder.Configuration.GetSection("Webhook"));

    // Register custom services
    builder.Services.AddSingleton<INotificationService, UdpNotificationService>();
    builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
    builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
    builder.Services.AddSingleton<IRabbitMQService, RabbitMQService>();
    builder.Services.AddSingleton<IWebhookHealthService, WebhookHealthService>();
    builder.Services.AddHostedService<Worker>();

    // Add Google Calendar Service
    builder.Services.AddSingleton(provider =>
    {
        var googleSettings = provider.GetRequiredService<IOptions<GoogleSettings>>().Value;
        var credentials = GoogleCredential.FromFile(googleSettings.ServiceAccountKeyPath)
            .CreateScoped(CalendarService.Scope.Calendar);
        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credentials,
            ApplicationName = "CalendarSyncService"
        });
    });

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
