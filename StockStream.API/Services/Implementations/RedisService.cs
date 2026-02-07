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

    public RedisService(IConnectionMultiplexer redis)
    {
        // Get the Redis database from cloud connection
        // IConnectionMultiplexer is already connected to Redis Labs in Program.cs
        // i don't see it on program.cs file , where is it ?
        // In Program.cs, we have the following code that sets up the Redis connection and registers it as a singleton service:
        /*
          try
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string 'Redis' not found");

    var redis = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    Console.WriteLine("[Startup] ✓ Redis connected successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] ⚠️  Redis connection warning: {ex.Message}");
    Console.WriteLine("[Startup] The application will start but caching will be unavailable");
}
        */
        // above ones ? 
        // yes, that code establishes the connection to Redis Labs and registers the IConnectionMultiplexer in the dependency injection container.
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
