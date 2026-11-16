# Builds and runs the Subfuse server. Data (database and encryption keys) lives in /data: mount a volume there.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/Subfuse.Server.csproj server/
RUN dotnet restore server/Subfuse.Server.csproj
COPY server/ server/
RUN dotnet publish server/Subfuse.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/subfuse.db" \
    DataProtection__KeysPath=/data/keys
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Subfuse.Server.dll"]
