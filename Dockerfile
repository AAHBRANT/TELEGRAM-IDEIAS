# Bot de Ideias (Telegram) — independente do app de SST. Contexto de build: esta pasta (bot-ideias/).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/BotIdeias.csproj src/
RUN dotnet restore src/BotIdeias.csproj
COPY src/ src/
RUN dotnet publish src/BotIdeias.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BotIdeias.dll"]
