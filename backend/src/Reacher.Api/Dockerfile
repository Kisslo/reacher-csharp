FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src

COPY global.json Directory.Build.props ./
COPY backend/src backend/src

RUN dotnet restore backend/src/Reacher.Api/Reacher.Api.csproj \
    --locked-mode
RUN dotnet publish backend/src/Reacher.Api/Reacher.Api.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish \
    --no-self-contained

# Matches the runtime in sdk:10.0.100. Bump both tags together.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Reacher.Api.dll"]
