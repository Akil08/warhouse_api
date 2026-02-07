using StockStream.API.DTOs;

namespace StockStream.API.Services.Interfaces;

/// <summary>
/// Interface for warehouse business logic
/// </summary>
public interface IWarehouseService
{
    /// <summary>
    /// Get products by category (uses Redis cache)
    /// </summary>
    Task<List<ProductResponseDto>> GetProductsByCategoryAsync(string category);

    /// <summary>
    /// Process purchase with transaction support
    /// </summary>
    Task<PurchaseResult> ProcessPurchaseAsync(int productId, int quantity);
}

/// <summary>
/// Result class for purchase operations with transaction rollback support
/// </summary>
public class PurchaseResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? NewStock { get; set; }

    public static PurchaseResult SuccessResult(int newStock) =>
        new() { Success = true, Message = "Purchase successful", NewStock = newStock };

    public static PurchaseResult FailureResult(string message) =>
        new() { Success = false, Message = message };
}
