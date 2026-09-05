using OrderProcessing.Shared.Models;
using Xunit;

namespace OrderProcessing.Tests;

public class OrderModelTests
{
    [Fact]
    public void NewOrder_DefaultsToPendingStatus()
    {
        var order = new Order { ProductName = "Widget", Quantity = 2 };

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void NewOrder_GetsAUniqueId()
    {
        var first = new Order();
        var second = new Order();

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void OrderCreatedMessage_CorrelationIdMatchesOrderId()
    {
        var orderId = Guid.NewGuid();
        var message = new OrderCreatedMessage { OrderId = orderId, ProductName = "Widget", Quantity = 1 };

        Assert.Equal(orderId.ToString(), message.CorrelationId);
    }
}
