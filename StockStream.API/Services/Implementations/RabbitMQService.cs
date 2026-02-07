using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Services.Implementations;

// what the fuck actually message means here ? 
// A message is a piece of data sent from one service to another via RabbitMQ.
// so its for ohter services to communicate with each other ? 
// yes, exactly. Messages allow different services to exchange information asynchronously.
// so asynchronousl , means they don't wait for answer, right ? 
// yes, the sender doesn't wait for the receiver to process the message. 
//This allows services to continue their work without being blocked.
// then wny not just use an restful api call ?
// RESTful API calls are synchronous, meaning the client waits for the server to respond.
// but we can use async, await , right ? does not that make the call asynchronously ? 
// Async/await in RESTful API calls only makes the client non-blocking, 
// but the server still processes each request synchronously.
// i did not  get the above line , tell me more preciouly / menaningfully !
// 
// RabbitMQ allows true asynchronous communication where the sender doesn't wait for the receiver at all.

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
/// so the backgrouud worker contineously check on quere server ?
/// 
/// 
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


// where do the IRabbitMQservice came from ? 
// opps, sorry , i missed that, thanks u  bro
// u have to say u r welcome
// no problem bro, i am here to help u , if u have any question just ask me


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
                // this name change per project , right ?
                // yes, the queue name should be relevant to your application's purpose
                // In this case, "low_stock_alerts" is used for stock alert messages
                // so worker check the msg with thse same name , right ?
                // yes, the AlertWorker listens to the "low_stock_alerts" queue to process incoming messages
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
            // this obj is send as emil to admin or warehouse manager , right ?
            // yes, the AlertWorker can use this message to notify admins about low stock
            // so this just what i want to sent in the message , right ?
            // yes, this object contains the relevant information for the low stock alert
            // this msg first sent to queue server , then worker get it from there , right ?
            // yes, the message is published to the CloudAMQP queue and later consumed by the AlertWorker
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
                // does this routing key mean the queue name ?
                // yes, in the case of the default direct exchange, the routing key is the name of the queue
                // so this key can very per project , right ?
                // yes, you should use a routing key that matches the name of the queue you want to send messages to
                // in this case, "low_stock_alerts" is the queue name
                // no i mean this key must match to the queue name in the worker file , right ?
                // yes, the AlertWorker listens to the "low_stock_alerts" queue, 
                // so the routing key here must match that queue name to ensure the message is delivered correctly
                // so both que name and key must be same , right ?
                // yes, when using the default direct exchange, the routing key must match the queue name       
                // what is default direct exchange ?
                // The default direct exchange is a pre-declared exchange in RabbitMQ that routes messages to queues based on the routing key.
                // When you publish a message to the default direct exchange with a specific routing key,
                // RabbitMQ delivers the message to the queue that has the same name as the routing key.                             
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

    // what applicantion stop here means ?
    // It refers to when the application is shutting down, either due to a manual stop, crash, or redeployment.
    // why we need to close the connection here ?   
    // Closing the connection properly ensures that resources are released and there are no memory leaks.
    public void Dispose()
    {
        // Close channel first
        _channel?.Close();
        // Then close connection
        _connection?.Close();
        Console.WriteLine("[RabbitMQ] ✓ Disconnected from CloudAMQP");
    }
}
