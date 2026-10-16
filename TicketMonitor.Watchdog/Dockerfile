FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY TicketMonitor.Core/TicketMonitor.Core.csproj TicketMonitor.Core/
COPY TicketMonitor.Watchdog/TicketMonitor.Watchdog.csproj TicketMonitor.Watchdog/

RUN dotnet restore TicketMonitor.Watchdog/TicketMonitor.Watchdog.csproj

COPY TicketMonitor.Core/ TicketMonitor.Core/
COPY TicketMonitor.Watchdog/ TicketMonitor.Watchdog/

RUN dotnet publish TicketMonitor.Watchdog/TicketMonitor.Watchdog.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8083
ENV TZ=Europe/Moscow
EXPOSE 8083

VOLUME ["/app/Data", "/app/Logs"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:8083/api/watchdog/health || exit 1

ENTRYPOINT ["dotnet", "TicketMonitor.Watchdog.dll"]