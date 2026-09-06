FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/OrderProcessing.Worker/OrderProcessing.Worker.csproj", "src/OrderProcessing.Worker/"]
COPY ["src/OrderProcessing.Shared/OrderProcessing.Shared.csproj", "src/OrderProcessing.Shared/"]
RUN dotnet restore "src/OrderProcessing.Worker/OrderProcessing.Worker.csproj"

COPY . .
RUN dotnet publish "src/OrderProcessing.Worker/OrderProcessing.Worker.csproj" -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OrderProcessing.Worker.dll"]
