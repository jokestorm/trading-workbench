FROM node:22-bookworm-slim AS codex
ARG CODEX_VERSION=0.162.1
RUN npm install --global --omit=dev @openai/codex@${CODEX_VERSION} \
    && codex --version \
    && codex exec --help | grep -q -- --ignore-user-config \
    && codex exec --help | grep -q -- --ignore-rules

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
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates libstdc++6 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=codex /usr/local/bin/node /usr/local/bin/node
COPY --from=codex /usr/local/lib/node_modules/@openai/ /usr/local/lib/node_modules/@openai/
RUN ln -s /usr/local/lib/node_modules/@openai/codex/bin/codex.js /usr/local/bin/codex \
    && codex --version
WORKDIR /app
COPY --from=build /out/ ./
COPY web/ ./web/
COPY deployment/run.sh /run.sh
ENV DataDirectory=/data/workbench \
    CodexHome=/data/workbench/codex \
    CodexExecutable=/usr/local/bin/codex \
    HomeAssistantIngress=true \
    ASPNETCORE_URLS=http://0.0.0.0:8099 \
    TZ=America/New_York
EXPOSE 8099
ENTRYPOINT ["/bin/sh", "/run.sh"]
