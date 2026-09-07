namespace OrderProcessing.Shared.Models;

/// <summary>
/// The status of an order as it moves through the async pipeline.
/// Pending   -> API accepted it and published the message, worker hasn't picked it up yet.
/// Processing-> Worker has received the message and is actively working on it.
/// Completed -> Worker finished successfully.
/// Failed    -> Worker gave up (after retries) and the message went to the dead-letter queue.
/// </summary>
public enum OrderStatus
{
    Pending,
    Processing,
    Completed,
    Failed 
}

/// <summary>
/// The order record itself. This is what gets stored in Redis (used here as a simple
/// status/lookup store) and returned by GET /orders/{id}.
///
/// NOTE FOR LEARNERS: in a real production system, Redis would sit in front of a
/// durable database (SQL/Cosmos DB) using the cache-aside pattern - Redis holds a
/// fast, disposable copy, and the database is the source of truth. This beginner
/// project intentionally uses Redis alone to keep the moving parts small. We call
/// this out explicitly so you don't copy this shortcut into a real production system
/// without adding a real database behind it.
/// </summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? FailureReason { get; set; }
}
