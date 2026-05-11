# ─────────────────────────────────────────────
# Aircane API — development image
# Build context: src/backend  (the solution root)
# ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev

WORKDIR /app

# Copy solution and all project files first for layer-cached restore
COPY Aircane.sln ./
COPY Aircane.Api/Aircane.Api.csproj ./Aircane.Api/
COPY Aircane.Application/Aircane.Application.csproj ./Aircane.Application/
COPY Aircane.Domain/Aircane.Domain.csproj ./Aircane.Domain/
COPY Aircane.Infrastructure/Aircane.Infrastructure.csproj ./Aircane.Infrastructure/
COPY Aircane.Workers/Aircane.Workers.csproj ./Aircane.Workers/

# Restore dependencies
RUN dotnet restore Aircane.Api/Aircane.Api.csproj

# Copy remaining source
COPY . .

# Build the project
RUN dotnet build Aircane.Api/Aircane.Api.csproj -c Debug --no-restore

EXPOSE 8080

WORKDIR /app/Aircane.Api

ENTRYPOINT ["dotnet", "run", "--no-build", "--urls", "http://0.0.0.0:8080"]
