namespace StockStream.API.Services.Interfaces;

/// <summary>
/// Interface for RabbitMQ message publishing
/// Handles sending low-stock alerts to CloudAMQP
/// </summary>
public interface IRabbitMQService
{
    /// <summary>
    /// Send low stock alert message to RabbitMQ queue
    /// </summary>
    Task SendLowStockAlertAsync(int productId, int stockLevel);
}
