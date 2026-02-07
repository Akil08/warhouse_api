using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Workers;

/// <summary>
/// ⭐ ALERT WORKER - BACKGROUND MESSAGE PROCESSING
/// 
/// THIS IS A BACKGROUND SERVICE THAT RUNS CONTINUOUSLY
/// 
/// KEY CONCEPTS:
/// 1. BackgroundService = .NET's built-in service for continuous background tasks
/// 2. Runs automatically when application starts
/// 3. Runs on a separate thread (doesn't block HTTP requests)
/// 4. Listens to RabbitMQ queue 24/7
/// 5. Processes messages from CloudAMQP as they arrive
/// 
//  what it's means  that it runs on separate thread ?
// It means that the AlertWorker operates on a different thread than the main thread that handles HTTP
// requests. 
//This allows the worker to run continuously in the background without blocking or slowing down the API's ability to
// respond to incoming HTTP requests. 
//The worker can listen for messages and process them independently while the API remains responsive to users.
/// does that means that it uses a thread non stop ? 
/// Yes, the AlertWorker runs on a separate thread that is active as long as the application is running.
/// so single thread language like node js can not run background worker ?
/// Node.js is single-threaded but can still run background tasks using its event loop and asynchronous programming model.
/// how come thats possible ?
/// Node.js uses an event-driven, non-blocking I/O model. 
/// It can handle background tasks by offloading them to the system's thread pool or using asynchronous callbacks, promises, or async/await.
/// This allows Node.js to perform background work without blocking the main thread, even though it is
/// MESSAGE FLOW:
/// Service publishes message → CloudAMQP queue stores it → Worker receives it → Worker processes it
/// 
/// LIFECYCLE:
/// 1. App starts → Worker StartAsync() → Connects to CloudAMQP
/// 2. ExecuteAsync() runs in infinite loop listening for messages
/// 3. Message arrives → Event handler triggers → Process message
/// 4. App stops → StopAsync() → Disconnect
/// 
/// WHY BACKGROUND WORKER?
/// - Product purchase API doesn't wait for alert processing
/// - Alerts are sent asynchronously (responsive API)
/// - Worker processes messages whenever they arrive
/// - Decoupled from HTTP request/response cycle
/// </summary>
public class AlertWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

    // what is _connection and _channel ?
    // _connection is the connection to the RabbitMQ server (CloudAMQP)
    // _channel is the communication channel used to send and receive messages from RabbitMQ
    private readonly ILogger<AlertWorker> _logger;

    public AlertWorker(IConfiguration configuration, ILogger<AlertWorker> logger)
    {
        _logger = logger;

        // ⭐ GET CLOUDAMQP CONNECTION STRING
        // Same URL as RabbitMQService, but this time we're CONSUMING (not publishing)
        string cloudAMQPUrl = configuration["RabbitMQ:ConnectionString"]
            ?? throw new InvalidOperationException("RabbitMQ connection string not configured");

        _logger.LogInformation("[AlertWorker] Initializing connection to CloudAMQP...");

        try
        {
            // ⭐ CREATE CONNECTION TO CLOUDAMQP
            var factory = new ConnectionFactory
            {
                Uri = new Uri(cloudAMQPUrl)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // ⭐ DECLARE SAME QUEUE (Consumer side)
            // This must match the queue declared in RabbitMQService
            // It's safe to declare multiple times - idempotent operation
            _channel.QueueDeclare(
                queue: "low_stock_alerts",
                durable: true,
                exclusive: false,
                autoDelete: false);

            _logger.LogInformation("[AlertWorker] ✓ Connected to CloudAMQP, listening to 'low_stock_alerts' queue");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[AlertWorker] ✗ Failed to initialize: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ⭐ MAIN EXECUTION METHOD
    /// 
    /// This method runs continuously in background after application starts
    /// It sets up message consumption and waits forever until app stops
    /// 
    /// EXECUTION FLOW:
    /// 1. Create consumer object that listens to queue
    /// 2. Register event handler for incoming messages
    /// 3. Tell RabbitMQ to start delivering messages
    /// 4. Loop forever (waiting for messages)
    /// 5. When message arrives, event handler executes
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[AlertWorker] ✓ ExecuteAsync started - Worker is now listening...");

        // ⭐ CREATE MESSAGE CONSUMER
        // EventingBasicConsumer = Asynchronous message consumer
        // It triggers events when messages arrive instead of polling
        var consumer = new EventingBasicConsumer(_channel);

        // ⭐ REGISTER MESSAGE RECEIVED EVENT HANDLER
        // This lambda function executes whenever CloudAMQP delivers a message
        // It runs on a separate thread pool thread
        consumer.Received += async (model, eventArgs) =>
        {
            try
            {
                _logger.LogInformation("[AlertWorker] Message received from CloudAMQP!");

                // ⭐ STEP 1: GET RAW MESSAGE BYTES FROM RABBITMQ
                // CloudAMQP delivers messages as byte arrays
                var body = eventArgs.Body.ToArray();

                // ⭐ STEP 2: CONVERT BYTES TO STRING
                // Decode UTF-8 bytes back to JSON string
                var messageJson = Encoding.UTF8.GetString(body);
                _logger.LogInformation($"[AlertWorker] Raw message: {messageJson}");

                // ⭐ STEP 3: DESERIALIZE JSON TO C# OBJECT
                // Parse JSON string into LowStockAlert object
                // If JSON format doesn't match, this throws exception
                // so we have json and we need to convert it to c# obj
                // so we createed a class in same file and pass the json to that class to convert it to c# obj , right ?
                // yes, the LowStockAlert class defines the structure of the message we expect to receive
                // and JsonSerializer.Deserialize converts the JSON string into an instance of LowStockAlert
                // so if class only used in this file , but not in other files, right ?
                // Yes, the LowStockAlert class is only used within this AlertWorker file to represent the structure of the messages received from RabbitMQ. It is not used in other parts of the application.
                var alert = JsonSerializer.Deserialize<LowStockAlert>(messageJson);

                if (alert == null)
                {
                    _logger.LogWarning("[AlertWorker] Failed to deserialize message");
                    _channel.BasicNack(eventArgs.DeliveryTag, false, false);
                    return;
                }

                // ⭐ STEP 4: PROCESS THE ALERT (Business Logic)
                // In real application, here you would:
                // - Send email to manager
                // - Create database alert record
                // - Call third-party notification service
                // - Update inventory system
                await ProcessLowStockAlert(alert);

                // ⭐ STEP 5: ACKNOWLEDGE MESSAGE PROCESSED
                // Tells CloudAMQP: "I successfully processed this message"
                // Removes message from queue permanently
                // If we don't acknowledge, message stays in queue and redelivers
                _channel.BasicAck(eventArgs.DeliveryTag, false);

                _logger.LogInformation($"[AlertWorker] ✓ Message processed and acknowledged for Product {alert.ProductId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AlertWorker] ✗ Error processing message: {ex.Message}");

                // ⭐ NEGATIVE ACKNOWLEDGE (NACK) ON ERROR
                // Tells CloudAMQP: "I failed to process this message"
                // Parameters:
                // - eventArgs.DeliveryTag: Which message to NACK
                // - multiple: false (only this message, not all)
                // - requeue: false (don't put back in queue, send to dead letter)
                _channel.BasicNack(eventArgs.DeliveryTag, false, false);
            }
        };

        // ⭐ START CONSUMING MESSAGES FROM CLOUDAMQP
        // This tells RabbitMQ to start delivering messages
        // autoAck = false means we manually acknowledge after processing
        _channel.BasicConsume(
            queue: "low_stock_alerts",
            autoAck: false,     // ⭐ IMPORTANT: Manual acknowledgment
                                // If true: RabbitMQ immediately removes message
                                // If false: We acknowledge after successful processing
            consumer: consumer);

        // ⭐ KEEP WORKER ALIVE
        // This loop runs forever until application stops (stoppingToken is cancelled)
        // The actual work happens in the event handler above
        _logger.LogInformation("[AlertWorker] Entering main loop - listening for messages...");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait 1 second before checking cancellation token again
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("[AlertWorker] StoppingToken received - Worker shutting down");
    }

    /// <summary>
    /// ⭐ PROCESS LOW STOCK ALERT - Business logic for handling alerts
    /// 
    /// This is called for each message received from CloudAMQP
    /// In production, this would send emails, create tickets, etc.
    /// </summary>
    private async Task ProcessLowStockAlert(LowStockAlert alert)
    {
        _logger.LogWarning("╔════════════════════════════════════════╗");
        _logger.LogWarning("║     ⚠️  LOW STOCK ALERT RECEIVED       ║");
        _logger.LogWarning("╚════════════════════════════════════════╝");
        _logger.LogWarning($"Product ID: {alert.ProductId}");
        _logger.LogWarning($"Current Stock: {alert.StockLevel}");
        _logger.LogWarning($"Threshold: {alert.Threshold}");
        _logger.LogWarning($"Alert Time: {alert.AlertTime:yyyy-MM-dd HH:mm:ss UTC}");
        _logger.LogWarning("────────────────────────────────────────");
        _logger.LogWarning("ACTION REQUIRED: Restock this product!");
        _logger.LogWarning("────────────────────────────────────────");

        // Simulate processing delay
        await Task.Delay(500);

        // In production, implement:
        // 1. Email notification
        // 2. SMS to manager
        // 3. Slack/Teams notification
        // 4. Database alert record
        // 5. Automated reorder from supplier
    }

    /// <summary>
    /// ⭐ CLEANUP ON APPLICATION SHUTDOWN
    /// 
    /// Called when application stops
    /// Ensures resources are properly released
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AlertWorker] StopAsync called - Closing CloudAMQP connection");

        _channel?.Close();
        _connection?.Close();

        _logger.LogInformation("[AlertWorker] ✓ Disconnected from CloudAMQP");

        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// ⭐ LOW STOCK ALERT DATA CLASS
/// 
/// This class represents the message format sent by RabbitMQService
/// Must match the JSON structure from RabbitMQService exactly
/// Properties are automatically populated by JsonSerializer.Deserialize
/// </summary>
/// 
/// 
/// where we used this class ?
/// 
public class LowStockAlert
{
    public int ProductId { get; set; }          // Which product is low
    public int StockLevel { get; set; }         // Current stock quantity
    public int Threshold { get; set; }          // Alert threshold
    public DateTime AlertTime { get; set; }     // When alert was created
}
