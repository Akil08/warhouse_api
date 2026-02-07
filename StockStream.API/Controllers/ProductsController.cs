using Microsoft.AspNetCore.Mvc;
using StockStream.API.DTOs;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IWarehouseService warehouseService, ILogger<ProductsController> logger)
    {
        _warehouseService = warehouseService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/products/{category}
    /// 
    /// Retrieve products by category with Redis caching
    /// 
    /// Logic flow:
    /// 1. Check Redis cache for category
    /// 2. If cache miss → Query PostgreSQL database
    /// 3. Cache results for 5 minutes
    /// 4. Return JSON list
    /// </summary>
    [HttpGet("{category}")]
    [ProduceResponseType(typeof(List<ProductResponseDto>), StatusCodes.Status200OK)]
    [ProduceResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetProductsByCategory(string category)
    {
        try
        {
            _logger.LogInformation($"GET /api/products/{category}");

            // Call service - Redis caching happens inside
            var products = await _warehouseService.GetProductsByCategoryAsync(category);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching products for category '{category}': {ex.Message}");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/products/buy
    /// 
    /// Process purchase with transaction and low-stock alert
    /// 
    /// Request body:
    /// {
    ///   "productId": 1,
    ///   "quantity": 3
    /// }
    /// 
    /// Response on success:
    /// {
    ///   "success": true,
    ///   "newStock": 47
    /// }
    /// 
    /// Logic flow:
    /// 1. Validate input (quantity > 0)
    /// 2. Start database transaction
    /// 3. Lock product row
    /// 4. Check if stock >= quantity
    /// 5. Update stock
    /// 6. Commit transaction
    /// 7. If stock ≤ 10 → Send RabbitMQ alert
    /// 8. Return result
    /// </summary>
    [HttpPost("buy")]
    [ProduceResponseType(typeof(PurchaseResult), StatusCodes.Status200OK)]
    [ProduceResponseType(typeof(PurchaseResult), StatusCodes.Status400BadRequest)]
    [ProduceResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Buy([FromBody] BuyRequestDto request)
    {
        try
        {
            // Validate request
            if (request.ProductId <= 0)
            {
                return BadRequest(PurchaseResult.FailureResult("Invalid ProductId"));
            }

            if (request.Quantity <= 0)
            {
                return BadRequest(PurchaseResult.FailureResult("Quantity must be greater than 0"));
            }

            _logger.LogInformation($"POST /api/products/buy - ProductId: {request.ProductId}, Quantity: {request.Quantity}");

            // Call service with transaction
            var result = await _warehouseService.ProcessPurchaseAsync(request.ProductId, request.Quantity);

            // Return success or failure
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing purchase: {ex.Message}");
            return StatusCode(500, PurchaseResult.FailureResult("Internal server error"));
        }
    }
}
