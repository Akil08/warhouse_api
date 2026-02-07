namespace StockStream.API.Services.Interfaces;

/// <summary>
/// Interface for Redis caching operations
/// </summary>
public interface IRedisService
{
    /// <summary>
    /// Get value from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Set value in cache with expiration
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

    /// <summary>
    /// Delete value from cache
    /// </summary>
    Task DeleteAsync(string key);
}
