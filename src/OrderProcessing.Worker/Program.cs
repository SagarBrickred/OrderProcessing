using Azure.Identity;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Worker;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

var serviceBusNamespace = builder.Configuration["ServiceBus:FullyQualifiedNamespace"]
    ?? throw new InvalidOperationException("Configuration value 'ServiceBus:FullyQualifiedNamespace' is missing.");

builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential()));

var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Redis connection string not found. Set the REDIS_CONNECTION_STRING environment variable " +
        "or, for local development, run: dotnet user-secrets set \"Redis:ConnectionString\" \"<value>\"");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.AddHostedService<OrderProcessingBackgroundService>();

var host = builder.Build();
host.Run();
