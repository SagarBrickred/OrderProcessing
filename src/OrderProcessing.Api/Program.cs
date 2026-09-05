using Azure.Identity;
using Azure.Messaging.ServiceBus;
using OrderProcessing.Api.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// --- Service Bus client ---
// We connect using only the *namespace hostname* (e.g. sb-orderproc-dev01.servicebus.windows.net),
// which is not a secret - it's just a DNS name. Authentication happens via DefaultAzureCredential,
// which automatically uses AKS Workload Identity when running in the cluster (no connection
// string, no shared access key, nothing secret involved at all).
var serviceBusNamespace = builder.Configuration["ServiceBus:FullyQualifiedNamespace"]
    ?? throw new InvalidOperationException("Configuration value 'ServiceBus:FullyQualifiedNamespace' is missing.");

builder.Services.AddSingleton(_ => new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential()));

// --- Redis connection ---
// UNLIKE Service Bus, Azure Managed Redis in this project is accessed via a connection
// string, because that's the credential type Redis needs. That connection string IS a
// secret, and it is deliberately never in this file, appsettings.json, or source control.
// In AKS it arrives as the environment variable REDIS_CONNECTION_STRING, populated by the
// Key Vault CSI driver + SecretProviderClass (set up in a later phase). Locally, use
// `dotnet user-secrets set "Redis:ConnectionString" "<value>"` instead.
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Redis connection string not found. Set the REDIS_CONNECTION_STRING environment variable " +
        "or, for local development, run: dotnet user-secrets set \"Redis:ConnectionString\" \"<value>\"");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.AddSingleton<IOrderStore, RedisOrderStore>();
builder.Services.AddSingleton<IOrderPublisher, ServiceBusOrderPublisher>();

// --- Health checks ---
// A pod is only "ready" to receive traffic if it can actually reach its dependencies.
// AddCheck below wires a lightweight custom Redis ping into GET /health.
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
