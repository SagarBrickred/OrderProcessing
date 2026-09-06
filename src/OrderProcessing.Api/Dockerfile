# --- Build stage ---
# Uses the full SDK image (has compilers etc.) - this stage is discarded after build,
# so it's fine that it's large.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the .csproj files first so Docker can cache the expensive `dotnet restore`
# layer separately from the rest of the source code - if only your .cs files change,
# restore doesn't re-run.
COPY ["src/OrderProcessing.Api/OrderProcessing.Api.csproj", "src/OrderProcessing.Api/"]
COPY ["src/OrderProcessing.Shared/OrderProcessing.Shared.csproj", "src/OrderProcessing.Shared/"]
RUN dotnet restore "src/OrderProcessing.Api/OrderProcessing.Api.csproj"

COPY . .
RUN dotnet publish "src/OrderProcessing.Api/OrderProcessing.Api.csproj" -c Release -o /app/publish

# --- Runtime stage ---
# Uses the much smaller ASP.NET runtime-only image - no SDK/compilers ship in the
# final image, which reduces size and attack surface.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Run as a non-root user inside the container - a defense-in-depth measure so that
# even if the app process were compromised, it doesn't run as root inside the pod.
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "OrderProcessing.Api.dll"]
