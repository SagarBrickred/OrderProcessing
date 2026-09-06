using System.Text.Json;
using StackExchange.Redis;
using Order = OrderProcessing.Shared.Models.Order;

namespace OrderProcessing.Api.Services;

/// <summary>
/// Stores order records in Redis as JSON strings, keyed by "order:{id}".
/// The Worker project uses the exact same key format so both processes agree
/// on where to find/update a given order's status.
///
/// Cache expiration: we set a 24-hour TTL. Orders are a transient status
/// lookup here, not permanent history - in a production system this data
/// would also be written to a durable database with no expiry.
/// </summary>
public class RedisOrderStore : IOrderStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private readonly IDatabase _db;
    private readonly ILogger<RedisOrderStore> _logger;

    public RedisOrderStore(IConnectionMultiplexer redis, ILogger<RedisOrderStore> logger)
    {
        _db = redis.GetDatabase();
        _logger = logger;
    }

    private static string KeyFor(Guid orderId) => $"order:{orderId}";

    public async Task SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(order);
        await _db.StringSetAsync(KeyFor(order.Id), json, Ttl);
        _logger.LogInformation("Redis lookup: saved order {OrderId} with status {Status}", order.Id, order.Status);
    }

    public async Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var json = await _db.StringGetAsync(KeyFor(orderId));
        if (json.IsNullOrEmpty)
        {
            _logger.LogInformation("Redis lookup: order {OrderId} not found (cache miss)", orderId);
            return null;
        }

        return JsonSerializer.Deserialize<Order>(json!);
    }
}
