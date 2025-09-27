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

    // Register custom services
    builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();

    // Register Google Calendar Service
    builder.Services.AddSingleton(provider =>
    {
        var googleSettings = provider.GetRequiredService<IOptions<GoogleSettings>>().Value;

        if (string.IsNullOrEmpty(googleSettings.ServiceAccountKeyPath))
        {
            throw new ArgumentNullException(nameof(googleSettings.ServiceAccountKeyPath), "ServiceAccountKeyPath is not configured in appsettings.json");
        }

        using var stream = new FileStream(googleSettings.ServiceAccountKeyPath, FileMode.Open, FileAccess.Read);
        var credential = GoogleCredential.FromStream(stream)
            .CreateScoped(new[] {
                CalendarService.Scope.Calendar,
                CalendarService.Scope.CalendarEvents
            });

        return new CalendarService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "CalendarSyncService"
        });
    });

    // Register the Worker service
    IHost host = Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<Worker>();
    
            // Add configuration
            services.Configure<AppSettings>(hostContext.Configuration.GetSection("AppSettings"));
    
            // Add Google Calendar Service
            services.AddSingleton(provider =>
            {
                var clientSecrets = new ClientSecrets
                {
                    ClientId = hostContext.Configuration["AppSettings:Google:ClientId"],
                    ClientSecret = hostContext.Configuration["AppSettings:Google:ClientSecret"]
                };
    
                var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets,
                    new[] { CalendarService.Scope.CalendarReadonly },
                    "user",
                    CancellationToken.None).Result;
    
                return new CalendarService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Calendar Sync"
                });
            });
    
            services.AddSingleton<ICalendarWrapper, CalendarWrapper>();
            services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
        })
        .Build();
    
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
