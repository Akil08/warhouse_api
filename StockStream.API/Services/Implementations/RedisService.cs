using StackExchange.Redis;
using System.Text.Json;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;


public class RedisService : IRedisService
{
    private readonly IDatabase _db;

   
    public RedisService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {   
            //stringgetasynce is provided by librry itsefkl ? 
            // Yes, StringGetAsync is a method provided by the StackExchange.Redis library for retrieving values from Redis.
            var value = await _db.StringGetAsync(key);
            
            if (!value.HasValue)
            {
                return default;
            }

            // Deserialize JSON string back to object
            return JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis GET error for key '{key}': {ex.Message}");
            return default;
        }
    }

    
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        try
        {
            // Set default expiration to 5 minutes if not specified
            expiration ??= TimeSpan.FromMinutes(5);

            // Serialize object to JSON string
            var jsonValue = JsonSerializer.Serialize(value);

            // Store in Redis with expiration
            await _db.StringSetAsync(key, jsonValue, expiration);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis SET error for key '{key}': {ex.Message}");
        }
    }

    /// <summary>
    /// Delete value from Redis cache
    /// </summary>
    public async Task DeleteAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis DELETE error for key '{key}': {ex.Message}");
        }
    }
}
