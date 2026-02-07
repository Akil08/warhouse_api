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

    /// <summary>
    /// GET PRODUCTS BY CATEGORY - WITH REDIS CACHING
    /// 
    /// Caching flow:
    /// 1. Check Redis cache → Return if found
    /// 2. Cache miss → Query database
    /// 3. Store in Redis for 5 minutes
    /// 4. Return to client
    /// 
    /// Benefits:
    /// - Reduces database load
    /// - Fast response times (Redis is in-memory)
    /// - Cost savings (fewer db queries)
    /// </summary>
    public async Task<List<ProductResponseDto>> GetProductsByCategoryAsync(string category)
    {
        // Generate cache key
        string cacheKey = $"products:{category.ToLower()}";

        // ⭐ CACHE HIT - Return immediately
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

        // ⭐ STORE IN CACHE for 5 minutes
        await _redisService.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));

        return products;
    }

    /// <summary>
    /// ⭐ PROCESS PURCHASE WITH DATABASE TRANSACTION
    /// 
    /// THIS IS CRITICAL FOR PREVENTING RACE CONDITIONS (OVERSELLING)
    /// 
    /// PROBLEM WE'RE SOLVING:
    /// Imagine last 1 item in stock, and 2 customers buy simultaneously:
    /// 
    /// WITHOUT TRANSACTION:
    /// Customer A: Check stock → 1 item → Buy → Update to 0
    /// Customer B: Check stock → 1 item → Buy → Update to 0 (WRONG! Oversold!)
    /// 
    /// WITH TRANSACTION (What we do here):
    /// Customer A: BEGIN TRANSACTION → Lock product row → Check stock (1) → Buy → Update to 0 → COMMIT
    /// Customer B: BEGIN TRANSACTION → Wait for lock on product row → Transaction A commits → Lock acquired → Check stock (0) → REJECT → ROLLBACK
    /// 
    /// HOW TRANSACTIONS WORK:
    /// 1. BEGIN TRANSACTION: Start atomic operation
    /// 2. DATABASE LOCK: PostgreSQL locks the product row
    /// 3. READ DATA: Only one transaction can modify at a time
    /// 4. COMMIT: All changes permanent, next transaction can proceed
    /// 5. ROLLBACK: If error, undo all changes, release lock
    /// 
    /// ISOLATION LEVELS:
    /// - Default (Read Committed): Prevents dirty reads
    /// - We use this: Simple, good for most cases
    /// 
    /// WHAT GETS LOCKED:
    /// - The Product row in database
    /// - No other transaction can modify this product until lock released
    /// - Other products can be modified freely
    /// </summary>
    
    // so this return purchase result is a class that i have
    // but its in interface file
    // my ques is , how do this class is accessible in this file ?
    // i mean , i have a class called purchase result in interface file , but i can use it in this file without importing the namespace of interface file
    // because both the interface and implementation are in the same namespace (StockStream.API.Services), 
    // they can access each other's types without needing an explicit using statement. 
    // The C# compiler recognizes that they are part of the same namespace and allows them to reference each other directly. 

    public async Task<PurchaseResult> ProcessPurchaseAsync(int productId, int quantity)
    {
     
        // does this method save the data into redis ? 
        // No, this method does not save data into Redis.
        //why not ? does not this mehtod change product stock quantity ?
        //  This method updates the stock quantity in the PostgreSQL database.
        // so when do i need to update the data in redis ?
        // You would update the Redis cache after successfully processing the purchase and updating the database.
        // This is because the cache needs to reflect the latest stock quantity after the purchase.
        // In this method, after the transaction commits and the stock quantity is updated in the database,
        // you would call _redisService.DeleteAsync(cacheKey) to invalidate the cache for that product category.
        // This way, the next time someone requests products by category, it will be a cache
        // but there is no delete m]]]mehtod here , right ?
        // The DeleteAsync method is part of the IRedisService interface, but it is not called directly in this ProcessPurchaseAsync method.

        // Validate input
        if (quantity <= 0)
        {
            return PurchaseResult.FailureResult("Quantity must be greater than 0");
        }

        // ⭐ TRANSACTION START
        // This creates an atomic operation block
        // All DB operations inside must succeed together, or all rollback
        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            Console.WriteLine($"[Transaction] Started for Product {productId}, Quantity {quantity}");

            // ⭐ STEP 1: LOAD PRODUCT WITH IMPLICIT LOCK
            // After BEGIN TRANSACTION, this query acquires a lock on the product row
            // No other transaction can modify this product until we commit/rollback
            var product = await _dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
            {
                Console.WriteLine($"[Transaction] Product {productId} not found");
                await transaction.RollbackAsync();
                return PurchaseResult.FailureResult("Product not found");
            }

            // ⭐ STEP 2: CHECK STOCK (Within transaction, with lock)
            // This check is safe because the row is locked
            // Another transaction can't change stock while we're checking
            if (product.StockQuantity < quantity)
            {
                Console.WriteLine($"[Transaction] Insufficient stock. Available: {product.StockQuantity}, Requested: {quantity}");
                await transaction.RollbackAsync();
                return PurchaseResult.FailureResult(
                    $"Insufficient stock. Available: {product.StockQuantity}");
            }

            // ⭐ STEP 3: UPDATE STOCK (Write operation in transaction)
            // This UPDATE runs inside transaction with lock
            product.StockQuantity -= quantity;
            product.UpdatedAt = DateTime.UtcNow;

            // SaveChanges writes to database but DOESN'T commit yet
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[Transaction] Stock updated: {product.StockQuantity}");

            // ⭐ STEP 4: COMMIT TRANSACTION
            // This makes all changes permanent and releases the lock
            // Other transactions waiting for this product can now proceed
            await transaction.CommitAsync();

            // should not we have to delete cache here ?
            // but there is no code to delete cache here , right ?
            // 

                        // ⭐ STEP 4: COMMIT TRANSACTION
            await transaction.CommitAsync();
            
            // ⭐ INVALIDATE CACHE - Delete cached products for this category
            string cacheKey = $"products:{product.Category.ToLower()}";
            await _redisService.DeleteAsync(cacheKey);
            Console.WriteLine($"[Cache] Invalidated cache for category: {product.Category}");
            
            Console.WriteLine($"[Transaction] Committed successfully. New stock: {product.StockQuantity}");

            Console.WriteLine($"[Transaction] Committed successfully. New stock: {product.StockQuantity}");

            // ⭐ STEP 5: CHECK FOR LOW STOCK ALERT (After commit)
            // We check AFTER the transaction commits to ensure purchase was successful
            if (product.StockQuantity <= LowStockThreshold)
            {
                Console.WriteLine($"[Alert] Stock below threshold ({LowStockThreshold}). Triggering RabbitMQ alert...");

                // ⭐ SERVICE LAYER ORCHESTRATION
                // WarehouseService triggers RabbitMQ
                // Controller doesn't know about RabbitMQ (Clean Architecture)
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
            // ⭐ TRANSACTION ROLLBACK ON ERROR
            // If anything goes wrong, rollback undoes all changes
            // Stock is restored, lock is released
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
