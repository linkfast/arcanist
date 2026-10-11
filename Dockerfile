# Arcanist server with the admin panel, in one image.
#
#   docker build -t arcanist .
#   docker run -d -p 8080:8080 -v arcanist-data:/data -v /path/to/music:/music:ro arcanist
#
# Then open http://<host>:8080/ and follow the setup wizard; it asks for the setup code printed in
# `docker logs <container>`. Add /music (or its subfolders) as a library.

FROM node:24-alpine AS panel
WORKDIR /src/Arcanist.Web
COPY src/Arcanist.Web/package.json src/Arcanist.Web/package-lock.json ./
RUN npm ci
COPY src/Arcanist.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Arcanist.Core/Arcanist.Core.csproj src/Arcanist.Core/
RUN dotnet restore src/Arcanist.Core/Arcanist.Core.csproj
COPY src/Arcanist.Core/ src/Arcanist.Core/
RUN dotnet publish src/Arcanist.Core/Arcanist.Core.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=server /app ./
COPY --from=panel /src/Arcanist.Web/dist/arcanist-web/browser ./wwwroot/admin

# Data directory (database, keys, caches, logs). Never put it on a network share when using SQLite.
ENV ARCANIST_DATA_DIR=/data
RUN mkdir -p /data && chown app:app /data
VOLUME ["/data"]

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Arcanist.Core.dll"]
