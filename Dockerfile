# Arcanist server with the admin panel, in one image.
#
#   docker build -t arcanist .
#   docker run -d -p 8484:8484 -v arcanist-data:/data -v /path/to/music:/music:ro arcanist
#
# Multi-architecture (what the release workflow publishes to ghcr.io/linkfast/arcanist):
#   docker buildx build --platform linux/amd64,linux/arm64 -t arcanist .
#
# Then open http://<host>:8484/ and follow the setup wizard; it asks for the setup code printed in
# `docker logs <container>`. Add /music (or its subfolders) as a library.

# The panel and the server are built once, on the build machine's platform; only the final stage is per target.
FROM --platform=$BUILDPLATFORM node:24-alpine AS panel
WORKDIR /src/Arcanist.Web
COPY src/Arcanist.Web/package.json src/Arcanist.Web/package-lock.json ./
RUN npm ci
COPY src/Arcanist.Web/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS server
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Arcanist.Core/Arcanist.Core.csproj src/Arcanist.Core/
# The runtime identifier only selects native libraries (SkiaSharp); the server itself is portable.
RUN echo "linux-$([ "$TARGETARCH" = amd64 ] && echo x64 || echo "$TARGETARCH")" > /rid \
    && dotnet restore src/Arcanist.Core/Arcanist.Core.csproj -r "$(cat /rid)"
COPY src/Arcanist.Core/ src/Arcanist.Core/
RUN dotnet publish src/Arcanist.Core/Arcanist.Core.csproj -c Release -o /app --no-restore \
    -r "$(cat /rid)" --self-contained false -p:UseAppHost=false
RUN mkdir /data

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=panel /src/Arcanist.Web/dist/arcanist-web/browser ./wwwroot/admin

# Data directory (database, keys, caches, logs). Never put it on a network share when using SQLite.
ENV ARCANIST_DATA_DIR=/data
# The server listens on 8484 everywhere (appsettings.json); drop the base image's 8080.
ENV ASPNETCORE_HTTP_PORTS=
# No RUN in this stage, so building for another architecture needs no emulation.
COPY --from=server --chown=app:app /data /data
VOLUME ["/data"]

USER app
EXPOSE 8484
ENTRYPOINT ["dotnet", "arcanist.dll"]
