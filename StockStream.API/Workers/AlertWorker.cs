using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StockStream.API.Services.Interfaces;

namespace StockStream.API.Workers;

public class AlertWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IModel _channel;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[AlertWorker] ✓ ExecuteAsync started - Worker is now listening...");

        var consumer = new EventingBasicConsumer(_channel);

        consumer.Received += async (model, eventArgs) =>
        {
            try
            {
                _logger.LogInformation("[AlertWorker] Message received from CloudAMQP!");

                var body = eventArgs.Body.ToArray();

                var messageJson = Encoding.UTF8.GetString(body);
                _logger.LogInformation($"[AlertWorker] Raw message: {messageJson}");

                var alert = JsonSerializer.Deserialize<LowStockAlert>(messageJson);

                if (alert == null)
                {
                    _logger.LogWarning("[AlertWorker] Failed to deserialize message");
                    _channel.BasicNack(eventArgs.DeliveryTag, false, false);
                    return;
                }

                await ProcessLowStockAlert(alert);

                _channel.BasicAck(eventArgs.DeliveryTag, false);

                _logger.LogInformation($"[AlertWorker] ✓ Message processed and acknowledged for Product {alert.ProductId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AlertWorker] ✗ Error processing message: {ex.Message}");

               
                _channel.BasicNack(eventArgs.DeliveryTag, false, false);
            }
        };

        _channel.BasicConsume(
            queue: "low_stock_alerts",
            autoAck: false,     
            consumer: consumer);

        _logger.LogInformation("[AlertWorker] Entering main loop - listening for messages...");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait 1 second before checking cancellation token again
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("[AlertWorker] StoppingToken received - Worker shutting down");
    }

    private async Task ProcessLowStockAlert(LowStockAlert alert)
    {
        // Simulate processing delay
        await Task.Delay(500);

    }

    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[AlertWorker] StopAsync called - Closing CloudAMQP connection");

        _channel?.Close();
        _connection?.Close();

        _logger.LogInformation("[AlertWorker] ✓ Disconnected from CloudAMQP");

        await base.StopAsync(cancellationToken);
    }
}
public class LowStockAlert
{
    public int ProductId { get; set; }          // Which product is low
    public int StockLevel { get; set; }         // Current stock quantity
    public int Threshold { get; set; }          // Alert threshold
    public DateTime AlertTime { get; set; }     // When alert was created
}
