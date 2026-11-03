# Build context must be the apps/ directory (see docker-compose).
# Infrastructure references kb-scraper Contracts, so both trees are copied.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/BotFactory.slnx backend/
COPY backend/BotFactory.Api/BotFactory.Api.csproj backend/BotFactory.Api/
COPY backend/BotFactory.Core/BotFactory.Core.csproj backend/BotFactory.Core/
COPY backend/BotFactory.Infrastructure/BotFactory.Infrastructure.csproj backend/BotFactory.Infrastructure/
COPY backend/BotFactory.Tests/BotFactory.Tests.csproj backend/BotFactory.Tests/
COPY kb-scraper/BotFactory.KbScraper.Contracts/BotFactory.KbScraper.Contracts.csproj kb-scraper/BotFactory.KbScraper.Contracts/

WORKDIR /src/backend
RUN dotnet restore BotFactory.Api/BotFactory.Api.csproj

COPY backend/ ./
COPY kb-scraper/BotFactory.KbScraper.Contracts/ ../kb-scraper/BotFactory.KbScraper.Contracts/
RUN dotnet publish BotFactory.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BotFactory.Api.dll"]
