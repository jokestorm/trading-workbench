# Build on the runner architecture; publish portable IL for the target runtime.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /source
COPY src/TradingWorkbench/TradingWorkbench.csproj src/TradingWorkbench/
RUN case "$TARGETARCH" in arm64) rid=linux-arm64 ;; amd64) rid=linux-x64 ;; *) exit 1 ;; esac \
    && dotnet restore src/TradingWorkbench -r "$rid"
COPY src/TradingWorkbench/ src/TradingWorkbench/
RUN case "$TARGETARCH" in arm64) rid=linux-arm64 ;; amd64) rid=linux-x64 ;; *) exit 1 ;; esac \
    && dotnet publish src/TradingWorkbench -c Release -r "$rid" --no-restore --self-contained false -p:UseAppHost=false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
COPY web/ ./web/
COPY deployment/run.sh /run.sh
ENV DataDirectory=/data/workbench \
    HomeAssistantIngress=true \
    ASPNETCORE_URLS=http://0.0.0.0:8099 \
    TZ=America/New_York
EXPOSE 8099
ENTRYPOINT ["/bin/sh", "/run.sh"]
