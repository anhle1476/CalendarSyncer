namespace CalendarDesktop.Services;

/// <summary>
/// Defines the contract for debouncing operations
/// </summary>
public interface IDebounceService : IDisposable
{
    /// <summary>
    /// Debounces an operation by key, canceling previous operations with the same key
    /// </summary>
    /// <typeparam name="T">Type of data to pass to the action</typeparam>
    /// <param name="key">Unique key for the operation</param>
    /// <param name="data">Data to pass to the action</param>
    /// <param name="action">Action to execute after the delay</param>
    /// <param name="delay">Delay before executing the action</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DebounceAsync<T>(string key, T data, Func<T, CancellationToken, Task> action, TimeSpan delay, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cancels any pending debounced operation for the given key
    /// </summary>
    /// <param name="key">The key to cancel</param>
    void CancelDebounce(string key);
    
    /// <summary>
    /// Cancels all pending debounced operations
    /// </summary>
    void CancelAllDebounces();
}