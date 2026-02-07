namespace StockStream.API.Services.Interfaces;

/// <summary>
/// Interface for Redis caching operations
/// </summary>
public interface IRedisService
{   

    // so it has 3 methods , which called form where ?
    // GetAsync and SetAsync are called from WarehouseService to cache product data
    // DeleteAsync is called from WarehouseService when product stock changes to invalidate cache

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
    /// 
    /// // did any files called this method ?
    /// if yes, give me the code line where its called , the exact line
    // yes, it's called in WarehouseService.cs when a purchase is processed to invalidate the cache for that product category
    // here is the line: await _redisService.DeleteAsync($"products_category_{product.Category}");
    // 
    Task DeleteAsync(string key);
}
