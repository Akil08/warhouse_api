using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;

/// <summary>
/// ⭐ RABBITMQ SERVICE - CLOUD MESSAGING SYSTEM
/// 
/// THIS CLASS HANDLES ALL COMMUNICATION WITH CLOUDAMQP (RabbitMQ in the cloud)
/// 
/// KEY CONCEPTS:
/// 1. CloudAMQP is a cloud-hosted RabbitMQ service (no local installation needed)
/// 2. This service SENDS messages to a queue in the cloud
/// 3. A background worker (AlertWorker) RECEIVES and processes those messages
/// 4. Messages flow: Service → CloudAMQP Queue → Background Worker
/// 
/// WHAT IS CLOUDAMQP?
/// - RabbitMQ = Message broker that stores and delivers messages
/// - CloudAMQP = RabbitMQ hosted in the cloud (like AWS, Azure, etc.)
/// - Connection happens via AMQPS (secure AMQP protocol over SSL)
/// - Free tier: 1 million messages per month
/// 
/// WHY USE IT?
/// - Decouples services (API doesn't wait for alert processing)
/// - Handles temporary failures gracefully (messages stored in queue)
/// - Enables background processing without blocking HTTP responses
/// </summary>
public class RabbitMQService : IRabbitMQService
{
    // ⭐ RABBITMQ CONNECTION FIELDS
    // These represent persistent connections to CloudAMQP
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMQService(IConfiguration configuration)
    {
        // ⭐ STEP 1: GET CLOUDAMQP CONNECTION STRING FROM CONFIG
        // This URL comes from appsettings.json, which gets it from CloudAMQP dashboard
        // Format: amqps://username:password@host/vhost
        // Example: amqps://user:pass@chimpanzee.rmq.cloudamqp.com/vhost
        string cloudAMQPUrl = configuration["RabbitMQ:ConnectionString"] 
            ?? throw new InvalidOperationException("RabbitMQ connection string not configured");

        Console.WriteLine("[RabbitMQ] Connecting to CloudAMQP...");

        try
        {
            // ⭐ STEP 2: CREATE CONNECTION FACTORY FOR CLOUDAMQP
            // The ConnectionFactory is configured with CloudAMQP credentials
            var factory = new ConnectionFactory
            {
                Uri = new Uri(cloudAMQPUrl)  // Parses the full CloudAMQP URL
                                            // Automatically sets:
                                            // - Username and password
                                            // - Host and port
                                            // - Virtual host (/vhost)
                                            // - SSL/TLS encryption (amqps://)
            };

            // ⭐ STEP 3: ESTABLISH CONNECTION TO CLOUDAMQP
            // This creates a TCP connection to CloudAMQP servers
            // Connection is persistent - stays open until explicitly closed
            _connection = factory.CreateConnection();

            // ⭐ STEP 4: CREATE CHANNEL ON CONNECTION
            // A channel is a logical connection within the TCP connection
            // Multiple channels can share one connection
            // Each channel can publish/consume messages independently
            _channel = _connection.CreateModel();

            Console.WriteLine("[RabbitMQ] ✓ Connected to CloudAMQP successfully");

            // ⭐ STEP 5: DECLARE QUEUE IN CLOUDAMQP
            // Creates "low_stock_alerts" queue if it doesn't exist
            // This is idempotent - safe to call multiple times
            _channel.QueueDeclare(
                queue: "low_stock_alerts",           // Queue name in CloudAMQP
                durable: true,                       // ⭐ DURABLE = Survives server restart
                                                    // If false: messages lost on crash
                                                    // If true: messages persist on disk
                exclusive: false,                    // ⭐ EXCLUSIVE = Only one connection can use
                                                    // We use false so multiple services can access
                autoDelete: false,                   // ⭐ AUTODELETE = Remove when unused
                                                    // We use false to keep queue permanently
                arguments: null);                    // Optional queue arguments

            Console.WriteLine("[RabbitMQ] ✓ Queue 'low_stock_alerts' declared in CloudAMQP");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RabbitMQ] ✗ Connection failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ⭐ SEND LOW STOCK ALERT TO CLOUDAMQP QUEUE
    /// 
    /// This method is called by WarehouseService when stock drops below threshold
    /// 
    /// MESSAGE FLOW:
    /// 1. Create alert object with ProductId, StockLevel, timestamp
    /// 2. Serialize to JSON string
    /// 3. Convert to UTF-8 bytes (RabbitMQ uses binary protocol)
    /// 4. Publish to "low_stock_alerts" queue in CloudAMQP
    /// 5. AlertWorker continuously polls CloudAMQP and processes messages
    /// 
    /// KEY POINT: This is ASYNC and NON-BLOCKING
    /// The API doesn't wait for the alert to be processed
    /// The message is stored in CloudAMQP queue until AlertWorker gets it
    /// </summary>
    public async Task SendLowStockAlertAsync(int productId, int stockLevel)
    {
        try
        {
            // ⭐ STEP 1: CREATE ALERT MESSAGE OBJECT
            // This C# anonymous object will be converted to JSON
            var alertMessage = new 
            { 
                ProductId = productId,
                StockLevel = stockLevel,
                AlertTime = DateTime.UtcNow,
                Threshold = 10
            };

            Console.WriteLine($"[RabbitMQ] Creating alert: Product {productId}, Stock {stockLevel}");

            // ⭐ STEP 2: SERIALIZE C# OBJECT TO JSON STRING
            // JSON serialization is required for CloudAMQP compatibility
            // All consumers (AlertWorker) expect JSON format
            string jsonMessage = JsonSerializer.Serialize(alertMessage);

            // ⭐ STEP 3: CONVERT JSON STRING TO BYTE ARRAY
            // RabbitMQ works with bytes at the protocol level
            // UTF-8 encoding ensures special characters are handled correctly
            byte[] messageBody = Encoding.UTF8.GetBytes(jsonMessage);

            // ⭐ STEP 4: PUBLISH MESSAGE TO CLOUDAMQP QUEUE
            // This sends the message to CloudAMQP servers
            // Queue stores it until a consumer (AlertWorker) processes it
            _channel.BasicPublish(
                exchange: "",                       // ⭐ EXCHANGE = Message router
                                                    // Empty string = Default direct exchange
                                                    // Direct exchange routes to queue by name
                routingKey: "low_stock_alerts",    // ⭐ ROUTING KEY = Queue name in direct exchange
                                                    // Message goes to queue with this name
                basicProperties: null,              // Optional message properties (could add priority, ttl, etc)
                body: messageBody);                 // Actual message bytes

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

    // ⭐ CLEANUP ON APPLICATION SHUTDOWN
    // Called when application stops to properly close connections
    public void Dispose()
    {
        // Close channel first
        _channel?.Close();
        // Then close connection
        _connection?.Close();
        Console.WriteLine("[RabbitMQ] ✓ Disconnected from CloudAMQP");
    }
}
