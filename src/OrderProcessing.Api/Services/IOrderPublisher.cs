using OrderProcessing.Shared.Models;

namespace OrderProcessing.Api.Services;

public interface IOrderPublisher
{
    Task PublishOrderCreatedAsync(OrderCreatedMessage message, CancellationToken cancellationToken = default);
}
