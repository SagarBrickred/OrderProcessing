# OrderProcessing

A minimal asynchronous Order Processing system, built as the learning project for
an end-to-end Azure/AKS/CI-CD deployment.

## What's in here

```
OrderProcessing.sln
src/
  OrderProcessing.Shared/    Models shared by API and Worker (Order, OrderCreatedMessage)
  OrderProcessing.Api/       ASP.NET Core Web API: POST /orders, GET /orders/{id}, GET /health
  OrderProcessing.Worker/    Background service that consumes Service Bus messages
tests/
  OrderProcessing.Tests/     xUnit unit tests (run by CI later)
k8s/                          (empty for now - Kubernetes manifests added in a later phase)
.github/workflows/            (empty for now - GitHub Actions workflow added in a later phase)
```

## How the pieces fit together

1. A client sends `POST /orders` with a product name and quantity.
2. The API saves the order to Redis with status `Pending`, publishes an
   `OrderCreated` message to a Service Bus queue, and returns immediately
   (HTTP 202) with a `Location` header pointing at `GET /orders/{id}`.
3. The Worker (a separate process/pod) is independently listening to the same
   queue. When the message arrives, it marks the order `Processing` in Redis,
   does its (currently simulated) work, then marks it `Completed` or `Failed`.
4. The client can poll `GET /orders/{id}` at any time to see the current status.

Nothing in the API waits for the Worker - that's what "asynchronous" means here.

## Authentication - what's secret and what isn't

- **Service Bus**: the API/Worker connect using only the namespace's public hostname
  (not a secret) plus `DefaultAzureCredential`, which uses AKS Workload Identity
  when running in the cluster. No connection string is ever used for Service Bus.
- **Redis**: this does require a connection string (a real secret). It is read
  exclusively from the `REDIS_CONNECTION_STRING` environment variable at runtime -
  it is never written into any `appsettings.json` or committed to git. In AKS,
  that environment variable will be populated by the Key Vault CSI driver in a
  later phase. For local development, use .NET's user-secrets tool instead:

  ```
  cd src/OrderProcessing.Api
  dotnet user-secrets set "Redis:ConnectionString" "<your-connection-string>"

  cd ../OrderProcessing.Worker
  dotnet user-secrets set "Redis:ConnectionString" "<your-connection-string>"
  ```

  User secrets are stored outside the repo (in your user profile folder), so
  they're safe from accidental commits.

## Running it locally (optional, once you have real Azure resources)

You'll need a real Service Bus namespace/queue and a real Redis instance to run
this end-to-end (there are no local emulators wired up in this beginner version).
Once Phase 1+ has created those:

1. Update `ServiceBus:FullyQualifiedNamespace` in both `appsettings.json` files.
2. Set the Redis connection string via user-secrets (above).
3. Make sure you're logged in with `az login` locally, since `DefaultAzureCredential`
   falls back to your Azure CLI login when not running in AKS - and make sure your
   own user account has been granted the Service Bus data-plane role (this gets
   set up in the RBAC phase).
4. `dotnet run --project src/OrderProcessing.Api`
5. `dotnet run --project src/OrderProcessing.Worker`

## Pushing this to GitHub

```
cd OrderProcessing
git init
git add .
git commit -m "Initial OrderProcessing project skeleton"
git branch -M main
git remote add origin https://github.com/<your-username>/<your-repo>.git
git push -u origin main
```

Nothing in this repository is secret, so it's safe to push as-is to a public or
private repository either way.

## What's intentionally not here yet

- Kubernetes manifests (`k8s/`) - added once AKS exists and we know real resource names.
- The GitHub Actions workflow (`.github/workflows/`) - added once OIDC federation is set up.
- A real database behind Redis - this project uses Redis alone for simplicity; see the
  comment in `Order.cs` for why that's a simplification, not a production pattern.
