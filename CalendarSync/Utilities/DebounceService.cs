using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CalendarSync.Utilities;

/// <summary>
/// Service for debouncing operations to prevent redundant executions
/// </summary>
public class DebounceService : IDebounceService, IDisposable
{
    private readonly ILogger<DebounceService> _logger;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new();
    private readonly object _timersLock = new();
    private bool _disposed = false;

    public DebounceService(ILogger<DebounceService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Debounces an operation by key, canceling previous operations with the same key
    /// </summary>
    public async Task DebounceAsync<T>(string key, T data, Func<T, CancellationToken, Task> action, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DebounceService));
        }

        await Task.Run(() =>
        {
            lock (_timersLock)
            {
                // Cancel existing timer for this key if it exists
                if (_debounceTimers.TryGetValue(key, out var existingTimer))
                {
                    existingTimer.Dispose();
                    _logger.LogDebug("Cancelled existing debounce timer for key {Key}", key);
                }

                // Create new timer that will trigger action after delay
                var timer = new Timer(async _ =>
                {
                    try
                    {
                        _logger.LogDebug("Debounce timer expired for key {Key}. Executing action.", key);
                        await action(data, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in debounced action for key {Key}", key);
                    }
                    finally
                    {
                        // Clean up timer after execution
                        CleanupTimer(key);
                    }
                }, null, delay, Timeout.InfiniteTimeSpan);

                _debounceTimers[key] = timer;
                _logger.LogDebug("Started debounce timer for key {Key} with delay {DelayMs}ms", key, delay.TotalMilliseconds);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Cancels any pending debounced operation for the given key
    /// </summary>
    public void CancelDebounce(string key)
    {
        if (_disposed) return;

        lock (_timersLock)
        {
            if (_debounceTimers.TryGetValue(key, out var timer))
            {
                timer.Dispose();
                _debounceTimers.TryRemove(key, out _);
                _logger.LogDebug("Cancelled debounce timer for key {Key}", key);
            }
        }
    }

    /// <summary>
    /// Cancels all pending debounced operations
    /// </summary>
    public void CancelAllDebounces()
    {
        if (_disposed) return;

        lock (_timersLock)
        {
            foreach (var kvp in _debounceTimers)
            {
                kvp.Value.Dispose();
            }
            _debounceTimers.Clear();
            _logger.LogDebug("Cancelled all debounce timers");
        }
    }

    /// <summary>
    /// Gets the count of active debounce timers (for testing/monitoring)
    /// </summary>
    public int ActiveTimersCount => _debounceTimers.Count;

    /// <summary>
    /// Checks if a debounce timer is active for the given key
    /// </summary>
    public bool IsDebounceActive(string key)
    {
        return _debounceTimers.ContainsKey(key);
    }

    /// <summary>
    /// Cleans up a specific timer
    /// </summary>
    private void CleanupTimer(string key)
    {
        lock (_timersLock)
        {
            if (_debounceTimers.TryGetValue(key, out var timerToRemove))
            {
                timerToRemove.Dispose();
                _debounceTimers.TryRemove(key, out _);
                _logger.LogDebug("Cleaned up debounce timer for key {Key}", key);
            }
        }
    }

    /// <summary>
    /// Disposes all timers and resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        CancelAllDebounces();
        _disposed = true;
        _logger.LogDebug("DebounceService disposed");
    }
}