FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/FdroidMgr.Api/FdroidMgr.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnetruntime

FROM debian:trixie-slim AS runtime
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=dotnetruntime /usr/share/dotnet /usr/share/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="/usr/share/dotnet:${PATH}"

RUN dotnet --list-runtimes | grep -q Microsoft.AspNetCore.App

RUN sed -i 's/^Suites: trixie trixie-updates$/Suites: trixie/' /etc/apt/sources.list.d/debian.sources \
    && grep -q '^Suites: trixie$' /etc/apt/sources.list.d/debian.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends openjdk-21-jdk-headless libicu76 python3 python3-pip curl ca-certificates git unzip \
    && rm -rf /var/lib/apt/lists/*

ARG FDROIDSERVER_VERSION=2.4.5
ARG FDROIDSERVER_URL=https://files.pythonhosted.org/packages/f4/d8/7beac4add64c4b3d03dac01a073dc7c6beb69a7adbd4215bc8def3075d46/fdroidserver-2.4.5.tar.gz
ARG FDROIDSERVER_SHA256=f9b52646264c732678e32e37e23a995db20cc61d45622dda5830ce23255547f4
RUN pip3 install --no-cache-dir --break-system-packages \
        "fdroidserver @ ${FDROIDSERVER_URL}#sha256=${FDROIDSERVER_SHA256}" \
    && test "$(fdroid --version 2>&1)" = "${FDROIDSERVER_VERSION}"

ENV ANDROID_HOME=/opt/android-sdk
RUN mkdir -p "$ANDROID_HOME/build-tools" "$ANDROID_HOME/licenses" \
    && curl -fsSL -o /tmp/build-tools.zip https://dl.google.com/android/repository/build-tools_r35_linux.zip \
    && unzip -q /tmp/build-tools.zip -d /tmp/build-tools \
    && mv /tmp/build-tools/android-15 "$ANDROID_HOME/build-tools/35.0.0" \
    && rm -rf /tmp/build-tools.zip /tmp/build-tools \
    && printf '8933bad161af4178b1185d1a37fbf41ea5269c55\nd56f5187479451eabf01fb78af6dfcb131a6481e\n24333f8a63b6825ea9c5514f83c2829b004d1fee\n' > "$ANDROID_HOME/licenses/android-sdk-license" \
    && test -x "$ANDROID_HOME/build-tools/35.0.0/aapt" \
    && test -x "$ANDROID_HOME/build-tools/35.0.0/apksigner"
ENV PATH="$ANDROID_HOME/build-tools/35.0.0:$PATH"

WORKDIR /app
COPY --from=build /out ./

ENV FDROID_REPO_ROOT=/data/fdroidrepo
ENV FDROID_REPO_PATH=/data/fdroidrepo/repo
ENV FDROID_REPO_PUBLIC_PATH=/data/fdroidrepo/public
ENV FDROID_KEY_PATH=/data/keys
ENV FDROID_DB="Data Source=/data/fdroidmgr.db"
ENV FDROID_CONFIG_FILE=/config/appsettings.json

VOLUME ["/data", "/config"]

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -fs http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "FdroidMgr.Api.dll"]
