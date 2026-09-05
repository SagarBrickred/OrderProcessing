using System.Text.Json;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Api.Services;

/// <summary>
/// Publishes OrderCreated messages to the Service Bus queue. The API's job stops
/// the instant the message is confirmed published - it does NOT wait for the
/// Worker to process it. That's what makes this "asynchronous": the client gets
/// a fast response, and the actual work happens independently, later.
/// </summary>
public class ServiceBusOrderPublisher : IOrderPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusOrderPublisher> _logger;

    public ServiceBusOrderPublisher(ServiceBusClient client, IConfiguration configuration, ILogger<ServiceBusOrderPublisher> logger)
    {
        var queueName = configuration["ServiceBus:QueueName"]
            ?? throw new InvalidOperationException("Configuration value 'ServiceBus:QueueName' is missing.");
        _sender = client.CreateSender(queueName);
        _logger = logger;
    }

    public async Task PublishOrderCreatedAsync(OrderCreatedMessage message, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(message);
        var serviceBusMessage = new ServiceBusMessage(body)
        {
            // MessageId enables Service Bus's built-in duplicate detection if enabled
            // on the queue, and CorrelationId lets you trace this message end-to-end
            // in Azure Monitor / Log Analytics.
            MessageId = message.OrderId.ToString(),
            CorrelationId = message.CorrelationId,
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);

        _logger.LogInformation(
            "Order message published: OrderId={OrderId} CorrelationId={CorrelationId}",
            message.OrderId, message.CorrelationId);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
