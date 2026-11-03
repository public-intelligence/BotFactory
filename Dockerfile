FROM node:22-alpine AS frontend
WORKDIR /app
COPY apps/frontend/package.json apps/frontend/package-lock.json ./
RUN npm ci
COPY apps/frontend/ .
RUN npx ng build --configuration production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY apps/backend/BotFactory.slnx .
COPY apps/backend/BotFactory.Api/BotFactory.Api.csproj BotFactory.Api/
COPY apps/backend/BotFactory.Core/BotFactory.Core.csproj BotFactory.Core/
COPY apps/backend/BotFactory.Infrastructure/BotFactory.Infrastructure.csproj BotFactory.Infrastructure/
COPY apps/backend/BotFactory.Tests/BotFactory.Tests.csproj BotFactory.Tests/
RUN dotnet restore
COPY apps/backend/ .
RUN dotnet publish BotFactory.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=frontend /app/dist/frontend/browser ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "BotFactory.Api.dll"]
