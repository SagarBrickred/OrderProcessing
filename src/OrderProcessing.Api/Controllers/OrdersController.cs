using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Services;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderStore _orderStore;
    private readonly IOrderPublisher _orderPublisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderStore orderStore, IOrderPublisher orderPublisher, ILogger<OrdersController> logger)
    {
        _orderStore = orderStore;
        _orderPublisher = orderPublisher;
        _logger = logger;
    }

    public record CreateOrderRequest(string ProductName, int Quantity);

    /// <summary>
    /// Accepts an order, saves it as "Pending" in Redis, publishes an OrderCreated
    /// message to Service Bus, and returns immediately (202 Accepted) - it does NOT
    /// wait for the Worker to finish processing. That's the whole point of this
    /// architecture: the client isn't blocked on downstream processing time.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName) || request.Quantity <= 0)
        {
            return BadRequest("ProductName is required and Quantity must be greater than zero.");
        }

        var order = new Order
        {
            ProductName = request.ProductName,
            Quantity = request.Quantity,
            Status = OrderStatus.Pending
        };

        _logger.LogInformation("Order received: OrderId={OrderId} Product={Product} Quantity={Quantity}",
            order.Id, order.ProductName, order.Quantity);

        await _orderStore.SaveAsync(order, cancellationToken);

        var message = new OrderCreatedMessage
        {
            OrderId = order.Id,
            ProductName = order.ProductName,
            Quantity = order.Quantity
        };

        await _orderPublisher.PublishOrderCreatedAsync(message, cancellationToken);

        return AcceptedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    /// <summary>
    /// Returns the current status of an order. Because processing is async, calling
    /// this immediately after POST /orders will usually show "Pending" - poll it
    /// again after a moment to see "Processing" and then "Completed"/"Failed".
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderStore.GetAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }
}
