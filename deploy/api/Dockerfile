# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files for caching restore layer
COPY ["src/CleanArchCqrs.Domain/CleanArchCqrs.Domain.csproj", "src/CleanArchCqrs.Domain/"]
COPY ["src/CleanArchCqrs.Application/CleanArchCqrs.Application.csproj", "src/CleanArchCqrs.Application/"]
COPY ["src/CleanArchCqrs.Infrastructure/CleanArchCqrs.Infrastructure.csproj", "src/CleanArchCqrs.Infrastructure/"]
COPY ["src/CleanArchCqrs.API/CleanArchCqrs.API.csproj", "src/CleanArchCqrs.API/"]

RUN dotnet restore "src/CleanArchCqrs.API/CleanArchCqrs.API.csproj"

# Copy full source and publish
COPY . .
WORKDIR "/src/src/CleanArchCqrs.API"
RUN dotnet publish "CleanArchCqrs.API.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Production Runtime (Non-root user)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends poppler-utils bubblewrap util-linux && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Run under standard unprivileged user
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "CleanArchCqrs.API.dll"]
