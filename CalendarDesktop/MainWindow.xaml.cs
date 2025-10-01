using CalendarDesktop.Models;
using CalendarDesktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace CalendarDesktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IDatabaseService _databaseService;
        private readonly UdpListenerService _udpListenerService;
        private readonly IDebounceService _debounceService;
        private readonly DebounceSettings _debounceSettings;
        private readonly ILogger<MainWindow>? _logger;
        private readonly ObservableCollection<CalendarEvent> _events;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize services
            var services = ConfigureServices();
            _databaseService = services.GetRequiredService<IDatabaseService>();
            _udpListenerService = services.GetRequiredService<UdpListenerService>();
            _debounceService = services.GetRequiredService<IDebounceService>();
            _debounceSettings = services.GetRequiredService<IOptions<DebounceSettings>>().Value;
            _logger = services.GetService<ILogger<MainWindow>>();

            // Initialize events collection
            _events = new ObservableCollection<CalendarEvent>();
            EventsDataGrid.ItemsSource = _events;

            // Subscribe to UDP messages
            _udpListenerService.MessageReceived += OnUdpMessageReceived;

            // Load initial data
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        /// <summary>
        /// Configure dependency injection services
        /// </summary>
        /// <returns>Service provider</returns>
        private ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            // Settings
            services.Configure<AppSettings>(configuration);
            services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
            services.Configure<NotificationSettings>(configuration.GetSection("Notification"));
            services.Configure<DebounceSettings>(configuration.GetSection("Debounce"));

            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Services
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<UdpListenerService>();
            services.AddSingleton<IDebounceService, DebounceService>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Handle window loaded event
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Start UDP listener
                await _udpListenerService.StartAsync();
                _logger?.LogInformation("UDP listener started");

                // Load calendar events
                await LoadEventsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during window initialization");
                MessageBox.Show($"Error initializing application: {ex.Message}", 
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handle window closing event
        /// </summary>
        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Cancel any pending debounced operations
                _debounceService.CancelAllDebounces();
                
                await _udpListenerService.StopAsync();
                _udpListenerService.Dispose();
                _debounceService.Dispose();
                _logger?.LogInformation("Services stopped and disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during cleanup");
            }
        }

        /// <summary>
        /// Load calendar events from database
        /// </summary>
        private async Task LoadEventsAsync()
        {
            try
            {
                _logger?.LogInformation("Loading calendar events...");
                var events = await _databaseService.GetAllEventsAsync();
                
                _events.Clear();
                foreach (var evt in events)
                {
                    _events.Add(evt);
                }

                _logger?.LogInformation("Loaded {Count} calendar events", events.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error loading calendar events");
                MessageBox.Show($"Error loading calendar events: {ex.Message}", 
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Handle UDP message received event
        /// </summary>
        private void OnUdpMessageReceived(object? sender, string message)
        {
            // Update UI on the main thread
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var formattedMessage = $"[{timestamp}] {message}\n";
                
                UdpMessagesTextBox.AppendText(formattedMessage);
                UdpMessagesTextBox.ScrollToEnd();

                _logger?.LogInformation("UDP message received: {Message}", message);

                // If it's an event change notification, debounce the refresh
                if (message.StartsWith("EVENT_CHANGE") || message.StartsWith("SYNC_STATUS"))
                {
                    var delay = TimeSpan.FromMilliseconds(_debounceSettings.EventChangeDelayMs);
                    
                    // Use debounce to prevent multiple rapid refreshes
                    Task.Run(async () =>
                    {
                        await _debounceService.DebounceAsync(
                            "event_table_refresh", 
                            message, 
                            async (msg, cancellationToken) =>
                            {
                                _logger?.LogDebug("Debounced refresh triggered by message: {Message}", msg);
                                // Ensure UI updates happen on the UI thread
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    await LoadEventsAsync();
                                });
                            }, 
                            delay);
                    });
                }
            });
        }

        /// <summary>
        /// Handle refresh button click
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadEventsAsync();
        }

        /// <summary>
        /// Handle clear messages button click
        /// </summary>
        private void ClearMessagesButton_Click(object sender, RoutedEventArgs e)
        {
            UdpMessagesTextBox.Clear();
        }
    }
}