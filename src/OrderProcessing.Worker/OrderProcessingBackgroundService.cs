using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Shared.Models;
using StackExchange.Redis;

namespace OrderProcessing.Worker;

/// <summary>
/// Consumes OrderCreated messages from the Service Bus queue and "processes" them
/// (in this beginner project, processing just means simulated work + a status
/// update in Redis - swap ProcessOrderAsync's contents for real business logic
/// later).
///
/// RETRY / DEAD-LETTER STRATEGY:
/// Service Bus queues have a built-in max delivery count (default 10, configurable
/// at the queue level). Every time this handler throws instead of completing the
/// message, Service Bus redelivers it and increments ServiceBusReceivedMessage.DeliveryCount.
/// Once DeliveryCount exceeds our own lower threshold (5, chosen deliberately below
/// the queue's max so we control the failure path explicitly), we dead-letter the
/// message ourselves with a reason, rather than waiting for Service Bus to do it
/// silently at 10. Dead-lettered messages land in the queue's $DeadLetterQueue sub-queue
/// for later inspection - they are not lost.
///
/// IDEMPOTENCY:
/// Service Bus queues (in the default, non-session, "at-least-once" mode used here)
/// can deliver the same message more than once - e.g. if the worker crashes after
/// processing but before acknowledging. Before doing any work we check Redis for
/// whether this OrderId is already "Completed", and if so we just acknowledge and
/// skip re-processing. This makes the handler safe to run twice on the same message.
/// </summary>
public class OrderProcessingBackgroundService : BackgroundService
{
    private const int MaxHandlerAttempts = 5;

    private readonly ServiceBusClient _client;
    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderProcessingBackgroundService> _logger;
    private ServiceBusProcessor? _processor;

    public OrderProcessingBackgroundService(
        ServiceBusClient client,
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<OrderProcessingBackgroundService> logger)
    {
        _client = client;
        _redis = redis;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueName = _configuration["ServiceBus:QueueName"]
            ?? throw new InvalidOperationException("Configuration value 'ServiceBus:QueueName' is missing.");

        // AutoCompleteMessages = false: we decide explicitly whether to Complete,
        // Abandon (retry), or DeadLetter each message - this is what makes the
        // retry/DLQ strategy above possible.
        _processor = _client.CreateProcessor(queueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4
        });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleProcessorErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("Worker started, listening on queue '{QueueName}'", queueName);

        // Keep the background service alive until the host shuts down.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        OrderCreatedMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<OrderCreatedMessage>(args.Message.Body.ToString());
        }
        catch (JsonException ex)
        {
            // A message we can never successfully parse will never succeed on retry either -
            // dead-letter it immediately instead of burning through retry attempts.
            _logger.LogError(ex, "Message received: could not deserialize message {MessageId}, dead-lettering", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", ex.Message);
            return;
        }

        if (message is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", "Message body deserialized to null");
            return;
        }

        _logger.LogInformation("Message received: OrderId={OrderId} CorrelationId={CorrelationId} DeliveryCount={DeliveryCount}",
            message.OrderId, message.CorrelationId, args.Message.DeliveryCount);

        try
        {
            var db = _redis.GetDatabase();
            var key = $"order:{message.OrderId}";

            // Idempotency check (cache-aside read).
            var existingJson = await db.StringGetAsync(key);
            if (!existingJson.IsNullOrEmpty)
            {
                var existing = JsonSerializer.Deserialize<Order>(existingJson!);
                if (existing?.Status == OrderStatus.Completed)
                {
                    _logger.LogInformation("Order {OrderId} already completed, skipping duplicate delivery", message.OrderId);
                    await args.CompleteMessageAsync(args.Message);
                    return;
                }
            }

            await ProcessOrderAsync(message, db, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex) when (args.Message.DeliveryCount >= MaxHandlerAttempts)
        {
            _logger.LogError(ex,
                "Processing failed: OrderId={OrderId} after {DeliveryCount} attempts, sending to dead-letter queue",
                message.OrderId, args.Message.DeliveryCount);

            await MarkOrderFailedAsync(message.OrderId, ex.Message);
            await args.DeadLetterMessageAsync(args.Message, "MaxRetriesExceeded", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Processing failed: OrderId={OrderId}, attempt {DeliveryCount} of {MaxAttempts} - message will be retried",
                message.OrderId, args.Message.DeliveryCount, MaxHandlerAttempts);

            // Abandon returns the message to the queue immediately for redelivery
            // (subject to the queue's lock duration / retry policy).
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private async Task ProcessOrderAsync(OrderCreatedMessage message, IDatabase db, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Order processing started: OrderId={OrderId}", message.OrderId);

        // Mark as "Processing" so GET /orders/{id} reflects reality while work is in flight.
        var order = new Order
        {
            Id = message.OrderId,
            ProductName = message.ProductName,
            Quantity = message.Quantity,
            Status = OrderStatus.Processing
        };
        await db.StringSetAsync($"order:{message.OrderId}", JsonSerializer.Serialize(order), TimeSpan.FromHours(24));

        // --- Simulated business logic goes here ---
        // Replace this with real work: charge a payment, reserve inventory, call another
        // service, etc. It's deliberately a no-op delay in this beginner project.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        order.Status = OrderStatus.Completed;
        order.ProcessedAtUtc = DateTimeOffset.UtcNow;
        await db.StringSetAsync($"order:{message.OrderId}", JsonSerializer.Serialize(order), TimeSpan.FromHours(24));

        _logger.LogInformation("Processing completed: OrderId={OrderId}", message.OrderId);
    }

    private async Task MarkOrderFailedAsync(Guid orderId, string reason)
    {
        var db = _redis.GetDatabase();
        var existingJson = await db.StringGetAsync($"order:{orderId}");
        var order = existingJson.IsNullOrEmpty
            ? new Order { Id = orderId }
            : JsonSerializer.Deserialize<Order>(existingJson!)!;

        order.Status = OrderStatus.Failed;
        order.FailureReason = reason;
        await db.StringSetAsync($"order:{orderId}", JsonSerializer.Serialize(order), TimeSpan.FromHours(24));
    }

    private Task HandleProcessorErrorAsync(ProcessErrorEventArgs args)
    {
        // This fires for transport-level errors (e.g. can't reach Service Bus at all),
        // as opposed to HandleMessageAsync's exceptions, which are about a single message.
        _logger.LogError(args.Exception, "Service Bus processor error in {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
