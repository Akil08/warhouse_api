namespace StockStream.API.Services.Interfaces;

public interface IRedisService
{   

    Task<T?> GetAsync<T>(string key);

    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);

    Task DeleteAsync(string key);
}
