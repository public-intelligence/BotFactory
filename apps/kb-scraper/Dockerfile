# Build with .NET 10 SDK (Playwright Docker images may still ship an older SDK).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BotFactory.KbScraper.Contracts/BotFactory.KbScraper.Contracts.csproj BotFactory.KbScraper.Contracts/
COPY BotFactory.KbScraper/BotFactory.KbScraper.csproj BotFactory.KbScraper/
RUN dotnet restore BotFactory.KbScraper/BotFactory.KbScraper.csproj

COPY . .
RUN dotnet publish BotFactory.KbScraper/BotFactory.KbScraper.csproj -c Release -o /app /p:UseAppHost=false

# Runtime: Chromium + browser deps from Playwright image; add ASP.NET Core 10 to run the app.
FROM mcr.microsoft.com/playwright/dotnet:v1.50.0-noble
USER root
RUN curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin \
      --channel 10.0 \
      --runtime aspnetcore \
      --install-dir /usr/share/dotnet

WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
EXPOSE 8080
ENTRYPOINT ["dotnet", "BotFactory.KbScraper.dll"]
