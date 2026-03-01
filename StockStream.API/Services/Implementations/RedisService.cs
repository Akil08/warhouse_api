using StackExchange.Redis;
using System.Text.Json;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;

/// <summary>
/// Redis service implementation for caching
/// Uses Redis Labs cloud instance (30MB free tier)
/// </summary>
public class RedisService : IRedisService
{
    private readonly IDatabase _db;

    // IS _db is for database or what ? 
    // _db is the Redis database instance obtained from the IConnectionMultiplexer.
    // is simple term what is _db? 
    // _db is the main interface for performing Redis operations like GET, SET, DELETE.

    // thsi _db is not postgresql database right ?
    // No, _db is not a PostgreSQL database. It is a Redis database instance used for caching.
    // so its for talking wioth the cloud redis db , right ? 
    // Yes, _db is used to interact with the Redis database hosted on Redis Labs cloud.

    // why we need serialization and deserialization for rdis ?
    // Redis stores data as strings, so we need to serialize complex objects to 
    // JSON strings when saving to Redis, and deserialize them back to objects when retrieving from Redis.
    // This allows us to cache complex data structures in Redis while still being able to 
    // work with them as C# objects in our application.
    // so we can say that redis is a key value store and it stores data in string format , 
    // so we need to serialize our objects to string format before storing them in redis 
    // and deserialize them back to objects when retrieving from redis , right ?
    public RedisService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    /// <summary>
    /// Get value from Redis cache
    /// Returns null if key doesn't exist (cache miss)
    /// </summary>
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

    /// <summary>
    /// Set value in Redis cache with optional expiration
    /// Default expiration: 5 minutes
    /// </summary>
    /// 
    /// does this T valuse is product dto ?
    /// 
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
