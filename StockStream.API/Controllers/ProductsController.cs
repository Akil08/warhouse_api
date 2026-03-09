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

    
    [HttpGet("{category}")]
    public async Task<IActionResult> GetProductsByCategory(string category)
    {
        try
        {
            _logger.LogInformation($"GET /api/products/{category}");

            // Call service - Redis caching happens inside
            // i have a question , does this getproductsbycategoryasync method is in wrehouse service or in redis service ?
            var products = await _warehouseService.GetProductsByCategoryAsync(category);

            return Ok(products);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching products for category '{category}': {ex.Message}");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpPost("buy")]
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
