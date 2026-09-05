using OrderProcessing.Shared.Models;

namespace OrderProcessing.Api.Services;

public interface IOrderStore
{
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
}
