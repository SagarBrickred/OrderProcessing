namespace OrderProcessing.Shared.Models;

/// <summary>
/// This is the payload the API publishes to Service Bus, and the payload the
/// Worker deserializes when it receives a message. Keeping it as its own small
/// class (rather than reusing the full Order) means the API and Worker can each
/// evolve their own internal Order representation without breaking the message
/// contract between them - this is a common pattern in async messaging systems.
/// </summary>
public class OrderCreatedMessage
{
    public Guid OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>
    /// Used to correlate log lines across the API and the Worker for the same
    /// order, even though they run in different processes/pods. We reuse the
    /// OrderId as the correlation ID here since it's already unique per order.
    /// </summary>
    public string CorrelationId => OrderId.ToString();
}
