using Microsoft.EntityFrameworkCore;
using StockStream.API.Data;
using StockStream.API.DTOs;
using StockStream.API.Models;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;

/// <summary>
/// ⭐ WAREHOUSE SERVICE - BUSINESS LOGIC LAYER
/// 
/// This service handles:
/// 1. Product retrieval with Redis caching
/// 2. Purchase processing with database transactions
/// 3. Low-stock alert triggering
/// 
/// KEY RESPONSIBILITY: Prevents race conditions (overselling) using transactions
/// </summary>
/// 
/// 
/// did we use row level lock ? row level is optimistic , right ? 
/// In this implementation, we are using the default locking 
/// behavior of PostgreSQL when we query the product row within a transaction. 
/// This means that when we load the product row for update, 
/// PostgreSQL will acquire a row
// level lock on that specific product. 
// This is a pessimistic locking approach, as it prevents other 
//transactions from modifying the same product
// until the current transaction is committed or rolled back.
public class WarehouseService : IWarehouseService
{
    private readonly AppDbContext _dbContext;
    private readonly IRedisService _redisService;
    private readonly IRabbitMQService _rabbitMQService;

    private const int LowStockThreshold = 10;  // Threshold for triggering alerts

    public WarehouseService(
        AppDbContext dbContext,
        IRedisService redisService,
        IRabbitMQService rabbitMQService)
    {
        _dbContext = dbContext;
        _redisService = redisService;
        _rabbitMQService = rabbitMQService;
    }

    
    public async Task<List<ProductResponseDto>> GetProductsByCategoryAsync(string category)
    {
        // Generate cache key
        string cacheKey = $"products:{category.ToLower()}";

        var cachedProducts = await _redisService.GetAsync<List<ProductResponseDto>>(cacheKey);
        if (cachedProducts != null)
        {
            Console.WriteLine($"[Cache] HIT - Category: {category}");
            return cachedProducts;
        }

        Console.WriteLine($"[Cache] MISS - Category: {category}, querying database");

        // ⭐ CACHE MISS - Query database
        var products = await _dbContext.Products
            .Where(p => p.Category.ToLower() == category.ToLower())
            .Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Category = p.Category,
                Price = p.Price,
                StockQuantity = p.StockQuantity
            })
            .ToListAsync();
        await _redisService.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));

        return products;
    }

    
    public async Task<PurchaseResult> ProcessPurchaseAsync(int productId, int quantity)
    {

        
        if (quantity <= 0)
        {
            return PurchaseResult.FailureResult("Quantity must be greater than 0");
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            Console.WriteLine($"[Transaction] Started for Product {productId}, Quantity {quantity}");

           
           // this line has some issue , fix it later 
           
            var product = await _dbContext.Products
                .FromSqlRaw("SELECT * FROM Products WHERE Id = {0} FOR UPDATE", productId)
                .FirstOrDefaultAsync();

            if (product == null)
            {
                Console.WriteLine($"[Transaction] Product {productId} not found");

                await transaction.RollbackAsync();
                return PurchaseResult.FailureResult("Product not found");
            }

            if (product.StockQuantity < quantity)
            {
                Console.WriteLine($"[Transaction] Insufficient stock. Available: {product.StockQuantity}, Requested: {quantity}");
                await transaction.RollbackAsync();
                return PurchaseResult.FailureResult(
                    $"Insufficient stock. Available: {product.StockQuantity}");
            }

            product.StockQuantity -= quantity;
            product.UpdatedAt = DateTime.UtcNow;

            // SaveChanges writes to database but DOESN'T commit yet
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[Transaction] Stock updated: {product.StockQuantity}");

            
            await transaction.CommitAsync();

            
            await transaction.CommitAsync();
            
            string cacheKey = $"products:{product.Category.ToLower()}";
            await _redisService.DeleteAsync(cacheKey);
            Console.WriteLine($"[Cache] Invalidated cache for category: {product.Category}");
            
            Console.WriteLine($"[Transaction] Committed successfully. New stock: {product.StockQuantity}");

            Console.WriteLine($"[Transaction] Committed successfully. New stock: {product.StockQuantity}");

           
            if (product.StockQuantity <= LowStockThreshold)
            {
                Console.WriteLine($"[Alert] Stock below threshold ({LowStockThreshold}). Triggering RabbitMQ alert...");

                
                try
                {
                    await _rabbitMQService.SendLowStockAlertAsync(productId, product.StockQuantity);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Alert] Failed to send: {ex.Message}");
                    // Don't throw - alert failure shouldn't fail the purchase
                }
            }

            return PurchaseResult.SuccessResult(product.StockQuantity);
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[Transaction] Rolled back due to error: {ex.Message}");
            }
            catch
            {
                // Rollback itself might fail (already rolled back, etc)
            }

            return PurchaseResult.FailureResult($"Transaction failed: {ex.Message}");
        }
    }
}
