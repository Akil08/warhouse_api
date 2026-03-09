using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;



public class RabbitMQService : IRabbitMQService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    

    public RabbitMQService(IConfiguration configuration)
    {
        
        string cloudAMQPUrl = configuration["RabbitMQ:ConnectionString"] 
            ?? throw new InvalidOperationException("RabbitMQ connection string not configured");

        Console.WriteLine("[RabbitMQ] Connecting to CloudAMQP...");

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(cloudAMQPUrl)  
            };

            
            _connection = factory.CreateConnection();

            
            _channel = _connection.CreateModel();

            Console.WriteLine("[RabbitMQ] ✓ Connected to CloudAMQP successfully");

            
            _channel.QueueDeclare(
                
                queue: "low_stock_alerts",           
                durable: true,                      
                
                exclusive: false,                   
                autoDelete: false,   
                arguments: null);                

            Console.WriteLine("[RabbitMQ] ✓ Queue 'low_stock_alerts' declared in CloudAMQP");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RabbitMQ] ✗ Connection failed: {ex.Message}");
            throw;
        }
    }

    
    public async Task SendLowStockAlertAsync(int productId, int stockLevel)
    {
        try
        {
             var alertMessage = new 
            { 
                ProductId = productId,
                StockLevel = stockLevel,
                AlertTime = DateTime.UtcNow,
                Threshold = 10
            };

            Console.WriteLine($"[RabbitMQ] Creating alert: Product {productId}, Stock {stockLevel}");

            
            string jsonMessage = JsonSerializer.Serialize(alertMessage);

            
            byte[] messageBody = Encoding.UTF8.GetBytes(jsonMessage);

            
            _channel.BasicPublish(
                exchange: "",                       
                routingKey: "low_stock_alerts",    
                basicProperties: null,
                body: messageBody);

            Console.WriteLine($"[RabbitMQ] ✓ Alert published to CloudAMQP: {jsonMessage}");

            // Return completed task (needed for async signature)
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RabbitMQ] ✗ Failed to send alert: {ex.Message}");
            throw;
        }
    }

       public void Dispose()
    {
        // Close channel first
        _channel?.Close();
        // Then close connection
        _connection?.Close();
        Console.WriteLine("[RabbitMQ] ✓ Disconnected from CloudAMQP");
    }
}
